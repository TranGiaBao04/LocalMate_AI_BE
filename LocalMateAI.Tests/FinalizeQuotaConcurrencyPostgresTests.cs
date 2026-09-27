using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class FinalizeQuotaConcurrencyPostgresTests
{
    private const string ConnectionEnvironmentVariable = "LOCALMATE_TEST_CONNECTION";
    private static readonly DateTime Now = new(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FreeUser_TwoConcurrentFinalizes_ExactlyOneSucceeds()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var drafts = await SeedAsync(connectionString, userId, PlanCode.Free, finalizedCount: 0);

        try
        {
            var results = await FinalizeConcurrentlyAsync(connectionString, userId, drafts);

            Assert.Single(results, result => result.Status == FinalizeTripResultStatus.Success);
            Assert.Single(results, result =>
                result.Status == FinalizeTripResultStatus.SavedTripQuotaExceeded);
            await AssertTripCountsAsync(connectionString, userId, finalized: 1, draft: 1);
            await AssertSubscriptionUsageAsync(connectionString, userId, used: 1, limit: 1);
        }
        finally
        {
            await CleanupAsync(connectionString, userId);
        }
    }

    [Fact]
    public async Task TripPassAtTwo_TwoConcurrentFinalizes_StopsAtThree()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var drafts = await SeedAsync(connectionString, userId, PlanCode.TripPass, finalizedCount: 2);

        try
        {
            var results = await FinalizeConcurrentlyAsync(connectionString, userId, drafts);

            Assert.Single(results, result => result.Status == FinalizeTripResultStatus.Success);
            Assert.Single(results, result =>
                result.Status == FinalizeTripResultStatus.SavedTripQuotaExceeded);
            await AssertTripCountsAsync(connectionString, userId, finalized: 3, draft: 1);
        }
        finally
        {
            await CleanupAsync(connectionString, userId);
        }
    }

    [Fact]
    public async Task Membership_TwoConcurrentFinalizes_BothSucceed()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var drafts = await SeedAsync(connectionString, userId, PlanCode.Membership, finalizedCount: 0);

        try
        {
            var results = await FinalizeConcurrentlyAsync(connectionString, userId, drafts);

            Assert.All(results, result => Assert.Equal(FinalizeTripResultStatus.Success, result.Status));
            await AssertTripCountsAsync(connectionString, userId, finalized: 2, draft: 0);
        }
        finally
        {
            await CleanupAsync(connectionString, userId);
        }
    }

    [Fact]
    public async Task FreeUser_SoftDeletedFinalizedTrip_DoesNotConsumeQuota()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var drafts = await SeedAsync(
            connectionString,
            userId,
            PlanCode.Free,
            finalizedCount: 1,
            softDeleteFinalized: true);

        try
        {
            await using var scope = CreateScope(connectionString);
            var result = await scope.Command.ExecuteAsync(userId, drafts[0].Id);

            Assert.Equal(FinalizeTripResultStatus.Success, result.Status);
            await AssertTripCountsAsync(connectionString, userId, finalized: 1, draft: 1);
        }
        finally
        {
            await CleanupAsync(connectionString, userId);
        }
    }

    [Fact]
    public async Task FreeUser_HistoricalOverLimit_BlocksWithoutDeletingTrips()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var drafts = await SeedAsync(connectionString, userId, PlanCode.Free, finalizedCount: 5);

        try
        {
            await using var scope = CreateScope(connectionString);
            var result = await scope.Command.ExecuteAsync(userId, drafts[0].Id);

            Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, result.Status);
            Assert.Equal(5, result.Used);
            Assert.Equal(1, result.Limit);
            await AssertTripCountsAsync(connectionString, userId, finalized: 5, draft: 2);
        }
        finally
        {
            await CleanupAsync(connectionString, userId);
        }
    }

    private static async Task<FinalizeTripResult[]> FinalizeConcurrentlyAsync(
        string connectionString,
        Guid userId,
        IReadOnlyList<Trip> drafts)
    {
        await using var first = CreateScope(connectionString);
        await using var second = CreateScope(connectionString);

        return await Task.WhenAll(
            first.Command.ExecuteAsync(userId, drafts[0].Id),
            second.Command.ExecuteAsync(userId, drafts[1].Id));
    }

    private static async Task<Trip[]> SeedAsync(
        string connectionString,
        Guid userId,
        PlanCode planCode,
        int finalizedCount,
        bool softDeleteFinalized = false)
    {
        await using var context = CreateContext(connectionString);
        context.Users.Add(new User
        {
            Id = userId,
            FullName = "Finalize quota concurrency test",
            Email = $"finalize-quota-{userId:N}@localmate.test",
            Role = UserRole.User
        });

        if (planCode != PlanCode.Free)
        {
            context.UserSubscriptions.Add(new UserSubscription
            {
                UserId = userId,
                PlanCode = planCode,
                StartsAt = Now.AddDays(-1),
                EndsAt = Now.AddDays(7)
            });
        }

        context.Trips.AddRange(Enumerable.Range(0, finalizedCount)
            .Select(_ =>
            {
                var trip = Trip(userId, TripStatus.Finalized);
                trip.DeletedAt = softDeleteFinalized ? Now.AddHours(-1) : null;
                return trip;
            }));
        var drafts = new[]
        {
            Trip(userId, TripStatus.Draft),
            Trip(userId, TripStatus.Draft)
        };
        context.Trips.AddRange(drafts);
        await context.SaveChangesAsync();
        return drafts;
    }

    private static Trip Trip(Guid userId, TripStatus status) =>
        new()
        {
            UserId = userId,
            StartLatitude = 10.77,
            StartLongitude = 106.69,
            DurationHours = 3,
            BudgetMin = 0,
            BudgetMax = 300_000,
            Status = status,
            FinalizedAt = status == TripStatus.Finalized ? Now.AddHours(-1) : null
        };

    private static async Task AssertTripCountsAsync(
        string connectionString,
        Guid userId,
        int finalized,
        int draft)
    {
        await using var context = CreateContext(connectionString);
        var statuses = await context.Trips
            .AsNoTracking()
            .Where(trip => trip.UserId == userId && trip.DeletedAt == null)
            .Select(trip => trip.Status)
            .ToListAsync();
        Assert.Equal(finalized, statuses.Count(status => status == TripStatus.Finalized));
        Assert.Equal(draft, statuses.Count(status => status == TripStatus.Draft));
    }

    private static async Task AssertSubscriptionUsageAsync(
        string connectionString,
        Guid userId,
        int used,
        int? limit)
    {
        await using var context = CreateContext(connectionString);
        var service = new SubscriptionService(
            new UserRepository(context),
            new SubscriptionRepository(context),
            new UsageEventRepository(context),
            new TripRepository(context),
            new FixedTimeProvider(Now));

        var subscription = await service.GetMySubscriptionAsync(userId);

        Assert.NotNull(subscription);
        Assert.Equal(used, subscription.SavedTrips.Used);
        Assert.Equal(limit, subscription.SavedTrips.Limit);
    }

    private static async Task CleanupAsync(string connectionString, Guid userId)
    {
        await using var context = CreateContext(connectionString);
        await context.Trips.Where(trip => trip.UserId == userId).ExecuteDeleteAsync();
        await context.UserSubscriptions
            .Where(subscription => subscription.UserId == userId)
            .ExecuteDeleteAsync();
        await context.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
    }

    private static FinalizeScope CreateScope(string connectionString)
    {
        var context = CreateContext(connectionString);
        var command = new FinalizeTripCommand(
            new TripRepository(context),
            new SubscriptionRepository(context),
            new TripFinalizeQuotaExecutor(context),
            new FixedTimeProvider(Now));
        return new FinalizeScope(context, command);
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private sealed record FinalizeScope(AppDbContext Context, FinalizeTripCommand Command)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
