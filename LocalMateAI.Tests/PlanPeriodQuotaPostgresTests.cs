using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using static LocalMateAI.Tests.PlanVersionFoundationPostgresTests;

namespace LocalMateAI.Tests;

public sealed class PlanPeriodQuotaPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static TripRequestDto Request() => new(10.77, 106.69, 6, 0, 900000, [], TravelMode.Motorbike);
    [Fact]
    public async Task FiniteQuota_Masking_Concurrency_RenewalBoundary_AndCapacityUsePurchasedPeriod()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await UserAsync(c);
        var places = await PlacesAsync(c);
        var (low, lowV1) = await CustomAsync(c, "LOW", 250, 50000, 20, 2, 4);
        var lowOrder = await BoundOrderAsync(c, user.Id, low.Id, lowV1);
        await Settlement(c, new Clock(Now)).ApplyVerifiedPaymentAsync(new(lowOrder.ProviderOrderCode, lowOrder.Amount, true));
        var lowPeriod = await c.SubscriptionPeriods.AsNoTracking().SingleAsync();
        Assert.Equal(GenerateTripResultStatus.Success, (await GenerateAsync(db, user.Id, places, Now)).Status);
        var (high, highV1) = await CustomAsync(c, "HIGH", 300, 69000, 2, 1, 99);
        var highOrder = await BoundOrderAsync(c, user.Id, high.Id, highV1);
        await Settlement(c, new Clock(Now.AddDays(1))).ApplyVerifiedPaymentAsync(new(highOrder.ProviderOrderCode, highOrder.Amount, true));
        var highPeriod = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.PlanId == high.Id);

        // The last finite slot is serialized by the existing user row lock.
        var competing = await Task.WhenAll(
            GenerateAsync(db, user.Id, places, Now.AddDays(1)),
            GenerateAsync(db, user.Id, places, Now.AddDays(1)));
        Assert.Single(competing, r => r.Status == GenerateTripResultStatus.Success);
        Assert.Single(competing, r => r.Status == GenerateTripResultStatus.QuotaExceeded);
        Assert.Equal(GenerateTripResultStatus.QuotaExceeded,
            (await GenerateAsync(db, user.Id, places, Now.AddDays(2))).Status);
        var usage = new UsageEventRepository(c);
        Assert.Equal(1, await usage.CountForPeriodAsync(user.Id, lowPeriod.Id, UsageEventType.Generate));
        Assert.Equal(1, await usage.CountForPeriodAsync(user.Id, highPeriod.Id, UsageEventType.Generate));
        Assert.Equal(0, await usage.CountAsync(user.Id, UsageEventType.Generate, Now, Now.AddDays(31)));

        Assert.Equal(GenerateTripResultStatus.Success, (await GenerateAsync(db, user.Id, places, Now.AddDays(3))).Status);
        Assert.Equal(GenerateTripResultStatus.QuotaExceeded, (await GenerateAsync(db, user.Id, places, Now.AddDays(3))).Status);
        Assert.Equal(2, await usage.CountForPeriodAsync(user.Id, lowPeriod.Id, UsageEventType.Generate));
        var v2 = await PublishAsync(c, low.Id, 2, 59000, 20, 3, 1);
        var renewal = await BoundOrderAsync(c, user.Id, low.Id, v2);
        await Settlement(c, new Clock(Now.AddDays(3))).ApplyVerifiedPaymentAsync(new(renewal.ProviderOrderCode, renewal.Amount, true));
        var future = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == renewal.Id);
        Assert.Equal(lowPeriod.EndsAt, future.StartsAt);

        var drafts = await c.Trips.AsNoTracking().Where(t => t.UserId == user.Id).OrderBy(t => t.CreatedAt).ToListAsync();
        var beforeCommand = Finalize(c, Now.AddDays(3));
        Assert.Equal(FinalizeTripResultStatus.Success, (await beforeCommand.ExecuteAsync(user.Id, drafts[0].Id)).Status);
        Assert.Equal(FinalizeTripResultStatus.Success, (await beforeCommand.ExecuteAsync(user.Id, drafts[1].Id)).Status);

        var before = await GenerateAsync(db, user.Id, places, future.StartsAt.AddSeconds(-1));
        Assert.Equal(GenerateTripResultStatus.QuotaExceeded, before.Status);
        var after = await GenerateAsync(db, user.Id, places, future.StartsAt);
        Assert.Equal(GenerateTripResultStatus.Success, after.Status);
        Assert.Equal(1, await usage.CountForPeriodAsync(user.Id, future.Id, UsageEventType.Generate));
        Assert.Equal(2, await usage.CountForPeriodAsync(user.Id, lowPeriod.Id, UsageEventType.Generate));
        Assert.Equal(1, await usage.CountForPeriodAsync(user.Id, highPeriod.Id, UsageEventType.Generate));
        var me = await new SubscriptionService(new UserRepository(c), new SubscriptionRepository(c),
            usage, new TripRepository(c), new Clock(future.StartsAt),
            new LlmCallLogRepository(c), new FakeSystemSettingProvider()).GetMySubscriptionAsync(user.Id);
        Assert.Equal(3, me!.Usage.GenerateLimit);
        Assert.Equal(1, me.Usage.GenerateUsed);
        Assert.Equal(2, me.SavedTrips.Used);
        Assert.Equal(1, me.SavedTrips.Limit);

        // Lower capacity retains existing finalized trips and blocks additional saves.
        var command = Finalize(c, future.StartsAt);
        Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, (await command.ExecuteAsync(user.Id, drafts[2].Id)).Status);
        Assert.Equal(2, await new TripRepository(c).CountFinalizedByUserAsync(user.Id));
        await new TripRepository(c).SoftDeleteAsync(drafts[0].Id, user.Id);
        Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, (await command.ExecuteAsync(user.Id, drafts[2].Id)).Status);
        await new TripRepository(c).SoftDeleteAsync(drafts[1].Id, user.Id);
        Assert.Equal(FinalizeTripResultStatus.Success, (await command.ExecuteAsync(user.Id, drafts[2].Id)).Status);
        Assert.Equal(lowPeriod.EndsAt, (await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.Id == lowPeriod.Id)).EndsAt);
    }
    [Theory]
    [InlineData(0, GenerateTripResultStatus.QuotaExceeded, 0)]
    [InlineData(1, GenerateTripResultStatus.Success, 1)]
    [InlineData(null, GenerateTripResultStatus.Success, 0)]
    public async Task PaidZeroFiniteUnlimited_ChargesOnlyFiniteEffectiveBucket(int? limit, GenerateTripResultStatus expected, int events)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await UserAsync(c);
        var places = await PlacesAsync(c);
        var (plan, version) = await CustomAsync(c, "EXPLORER", 300, 50000, 10, limit, 4);
        var order = await BoundOrderAsync(c, user.Id, plan.Id, version);
        await Settlement(c, new Clock(Now)).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true));
        Assert.Equal(expected, (await GenerateAsync(db, user.Id, places, Now)).Status);
        Assert.Equal(events, await c.UsageEvents.CountAsync());
        if (events > 0)
            Assert.Equal((await c.SubscriptionPeriods.SingleAsync()).Id, (await c.UsageEvents.SingleAsync()).SubscriptionPeriodId);
    }
    private static async Task<GenerateTripResult> GenerateAsync(IsolatedPlanDatabase db, Guid userId, Guid[] places, DateTime now)
    {
        var seed = new GenerateQuotaConcurrencyPostgresTests.SeedData(userId, places, now);
        await using var scope = GenerateQuotaConcurrencyPostgresTests.CreateScope(db.Connection, seed);
        return await scope.Service.GenerateAsync(userId, Request());
    }
    private static FinalizeTripCommand Finalize(AppDbContext c, DateTime now) => new(
        new TripRepository(c), new SubscriptionRepository(c), new TripFinalizeQuotaExecutor(c),
        new UserRepository(c), new EmailOutboxRepository(c), new Clock(now));
    private static async Task<Guid[]> PlacesAsync(AppDbContext c)
    {
        var places = Enumerable.Range(0, 2).Select(index => new Place
        {
            Name = $"Quota place {index}",
            Address = "Test",
            Category = PlaceCategory.Cafe,
            Status = PlaceStatus.Active,
            IsVerified = true,
            EstimatedCostMin = 20000,
            EstimatedCostMax = 50000,
            Location = new Point(106.69 + index * 0.001, 10.77) { SRID = 4326 }
        }).ToArray();
        c.Places.AddRange(places);
        await c.SaveChangesAsync();
        return places.Select(p => p.Id).ToArray();
    }
}
