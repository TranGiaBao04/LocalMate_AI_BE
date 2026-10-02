using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class SingleItineraryConcurrencyPostgresTests
{
    [Theory]
    [InlineData("webhook", "webhook")]
    [InlineData("webhook", "admin")]
    [InlineData("webhook", "worker")]
    [InlineData("admin", "worker")]
    public async Task SharedSettlement_OneGrantOneHistoryOneReceipt(string first, string second)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context(); var u = await PlanVersionFoundationPostgresTests.UserAsync(seed);
        var order = await SingleItineraryPostgresTests.Order(seed, u.Id);
        async Task Run(string caller)
        {
            await using var c = db.Context();
            var gateway = new SingleItineraryPostgresTests.Gateway();
            if (caller == "webhook")
                await new PaymentWebhookService(gateway, SingleItineraryPostgresTests.Settlement(c),
                    new PaymentEvidenceRepository(c), Options.Create(new PaymentEvidenceOptions()),
                    SingleItineraryPostgresTests.Clock, NullLogger<PaymentWebhookService>.Instance)
                    .ProcessAsync(order.ProviderOrderCode.ToString());
            else
                await new PaymentReconciliationService(new PaymentOrderRepository(c), gateway, SingleItineraryPostgresTests.Settlement(c),
                    SingleItineraryPostgresTests.Clock, NullLogger<PaymentReconciliationService>.Instance)
                    .ReconcileAsync(order.Id, new(caller == "admin" ? PaymentStatusChangeSource.AdminReconcile
                        : PaymentStatusChangeSource.BackgroundReconcile, caller == "admin" ? u.Id : null));
        }
        await Task.WhenAll(Run(first), Run(second));
        Assert.Equal(1, await seed.SingleItineraryEntitlements.CountAsync());
        Assert.Equal(1, await seed.PaymentOrderStatusHistories.CountAsync(h => h.ToStatus == PaymentOrderStatus.Paid));
        Assert.Equal(1, await seed.EmailOutboxMessages.CountAsync());
        Assert.Equal(new[] { first, second }.Count(s => s == "webhook"), await seed.PaymentWebhookReceipts.CountAsync());
        Assert.Empty(await seed.SubscriptionPeriods.ToListAsync());
    }

    [Theory]
    [InlineData("same_trip")]
    [InlineData("different_trip")]
    [InlineData("normal_finalize")]
    [InlineData("delete")]
    [InlineData("different_entitlement")]
    [InlineData("settlement")]
    public async Task ConsumeRaces_AtMostOneConsumptionWithoutPartialState(string race)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(seed);
        var trip = await PlanVersionFoundationPostgresTests.TripAsync(seed, u.Id);
        var e = await SingleItineraryPostgresTests.Grant(seed, u.Id);
        var otherTrip = await PlanVersionFoundationPostgresTests.TripAsync(seed, u.Id);
        var other = race == "different_entitlement" ? await SingleItineraryPostgresTests.Grant(seed, u.Id) : null;
        var pending = race == "settlement" ? await SingleItineraryPostgresTests.Order(seed, u.Id) : null;
        async Task<FinalizeTripResult> Consume(Guid tripId, Guid entitlement)
        {
            await using var c = db.Context();
            return await SingleItineraryPostgresTests.Finalize(c).ExecuteAsync(u.Id, tripId,
                new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = entitlement });
        }
        var a = Consume(trip.Id, e.Id);
        async Task Other()
        {
            await using var c = db.Context();
            if (race == "delete")
                await new TripDeletionService(new UserRepository(c), new TripRepository(c), new TripFinalizeQuotaExecutor(c)).DeleteAsync(u.Id, trip.Id);
            else if (race == "normal_finalize")
                await SingleItineraryPostgresTests.Finalize(c).ExecuteAsync(u.Id, trip.Id);
            else if (race == "settlement")
                await SingleItineraryPostgresTests.Settlement(c).ApplyVerifiedPaymentAsync(new(pending!.ProviderOrderCode, 29000, true));
            else await Consume(race == "different_trip" ? otherTrip.Id : trip.Id, other?.Id ?? e.Id);
        }
        await Task.WhenAll(a, Other());
        var consumed = await seed.SingleItineraryEntitlements.AsNoTracking().Where(x => x.ConsumedAt != null).ToListAsync();
        Assert.True(consumed.Count <= 1);
        if (race is not ("delete" or "normal_finalize")) Assert.Single(consumed);
        Assert.Equal(0, await seed.SubscriptionPeriods.CountAsync());
        Assert.Equal(consumed.Count, await seed.Trips.CountAsync(t => t.Status == TripStatus.Finalized)
            - (race == "normal_finalize" && consumed.Count == 0 ? 1 : 0));
        Assert.Equal(race == "settlement" ? 2 : 1, await seed.PaymentOrderStatusHistories.CountAsync(h => h.ToStatus == PaymentOrderStatus.Paid)
            - (race == "different_entitlement" ? 1 : 0));
    }

    [Fact]
    public async Task SameAttemptConcurrentCheckout_OnlyOneNetworkCreate()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(seed); var attempt = Guid.NewGuid();
        var g = new SingleItineraryPostgresTests.Gateway();
        async Task<Guid> Run()
        {
            await using var c = db.Context();
            return (await SingleItineraryPostgresTests.Purchases(c, g).CheckoutAsync(u.Id, attempt)).Response!.OrderId;
        }
        var results = await Task.WhenAll(Run(), Run());
        Assert.Equal(results[0], results[1]); Assert.Equal(1, g.Creates);
        Assert.Equal(1, await seed.PaymentOrders.CountAsync());
    }

    [Fact]
    public async Task DistinctEntitlements_ConcurrentOwnedDrafts_BothConsumeOutsideNormalCapacity()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(seed);
        var a = await SingleItineraryPostgresTests.Grant(seed, u.Id);
        var b = await SingleItineraryPostgresTests.Grant(seed, u.Id);
        var first = await PlanVersionFoundationPostgresTests.TripAsync(seed, u.Id);
        var second = await PlanVersionFoundationPostgresTests.TripAsync(seed, u.Id);
        async Task<FinalizeTripResultStatus> Run(Guid trip, Guid entitlement)
        {
            await using var c = db.Context();
            return (await SingleItineraryPostgresTests.Finalize(c).ExecuteAsync(u.Id, trip,
                new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = entitlement })).Status;
        }
        var results = await Task.WhenAll(Run(first.Id, a.Id), Run(second.Id, b.Id));
        Assert.All(results, result => Assert.Equal(FinalizeTripResultStatus.Success, result));
        Assert.Equal(2, await seed.SingleItineraryEntitlements.CountAsync(e => e.ConsumedAt != null));
        Assert.Equal(0, await new TripRepository(seed).CountNormalFinalizedByUserAsync(u.Id));
        Assert.Equal(4, await seed.EmailOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task ConsumeRollback_OutboxFailureRestoresDraftAndUnusedGrant()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PlanVersionFoundationPostgresTests.TripAsync(c, u.Id);
        var e = await SingleItineraryPostgresTests.Grant(c, u.Id);
        await c.Database.ExecuteSqlRawAsync("""CREATE FUNCTION test_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Controlled failure'; END $$; CREATE TRIGGER fail BEFORE INSERT ON "EmailOutboxMessages" FOR EACH ROW EXECUTE FUNCTION test_fail();""");
        await Assert.ThrowsAnyAsync<Exception>(() => SingleItineraryPostgresTests.Finalize(c).ExecuteAsync(u.Id, trip.Id,
            new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = e.Id }));
        c.ChangeTracker.Clear();
        Assert.Null((await c.SingleItineraryEntitlements.SingleAsync()).ConsumedAt);
        Assert.Equal(TripStatus.Draft, (await c.Trips.SingleAsync()).Status);
        Assert.Equal(1, await c.EmailOutboxMessages.CountAsync());
    }
}
