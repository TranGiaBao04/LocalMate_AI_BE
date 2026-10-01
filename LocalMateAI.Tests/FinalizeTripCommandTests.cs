using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class FinalizeTripCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Execute_DraftOwnedTrip_Succeeds()
    {
        var trip = Trip(TripStatus.Draft, UserId);
        var repository = new FakeTripRepository([trip]);

        var result = await Command(repository).ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(trip.Id, result.Response.TripId);
        Assert.Equal(nameof(TripStatus.Finalized), result.Response.Status);
    }

    [Fact]
    public async Task Execute_NullTrip_ReturnsNotFound()
    {
        var result = await Command(new FakeTripRepository([]))
            .ExecuteAsync(UserId, Guid.NewGuid());

        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Execute_TripOwnedByOtherUser_ReturnsNotFound()
    {
        var trip = Trip(TripStatus.Draft, OtherUserId);

        var result = await Command(new FakeTripRepository([trip]))
            .ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
    }

    [Fact]
    public async Task Execute_AlreadyFinalized_ReturnsAlreadyFinalized()
    {
        var trip = Trip(TripStatus.Finalized, UserId);

        var result = await Command(new FakeTripRepository([trip]))
            .ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.AlreadyFinalized, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Execute_EmptyTripId_ReturnsInvalidWithoutStartingTransaction()
    {
        var executor = new FakeQuotaExecutor();
        var command = Command(new FakeTripRepository([]), executor: executor);

        var result = await command.ExecuteAsync(UserId, Guid.Empty);

        Assert.Equal(FinalizeTripResultStatus.InvalidTripId, result.Status);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task Execute_ConcurrentFinalizeLosesRace_ReturnsNotFound()
    {
        var trip = Trip(TripStatus.Draft, UserId);
        var repository = new FakeTripRepository([trip], finalizeResult: false);

        var result = await Command(repository).ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
    }

    [Fact]
    public async Task Execute_FreeAtLimit_ReturnsQuotaExceededWithoutMutation()
    {
        var finalized = Trip(TripStatus.Finalized, UserId);
        var draft = Trip(TripStatus.Draft, UserId);
        var repository = new FakeTripRepository([finalized, draft]);

        var result = await Command(repository).ExecuteAsync(UserId, draft.Id);

        Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, result.Status);
        Assert.Equal(1, result.Used);
        Assert.Equal(1, result.Limit);
        Assert.Equal(0, repository.FinalizeCalls);
        Assert.Equal(TripStatus.Draft, draft.Status);
    }

    [Theory]
    [InlineData(2, FinalizeTripResultStatus.Success)]
    [InlineData(3, FinalizeTripResultStatus.SavedTripQuotaExceeded)]
    public async Task Execute_ActiveTripPass_EnforcesThreeTripLimit(
        int finalizedCount,
        FinalizeTripResultStatus expectedStatus)
    {
        var draft = Trip(TripStatus.Draft, UserId);
        var trips = Enumerable.Range(0, finalizedCount)
            .Select(_ => Trip(TripStatus.Finalized, UserId))
            .Append(draft)
            .ToArray();
        var subscription = Subscription(PlanCode.TripPass, Now.AddDays(7));

        var result = await Command(new FakeTripRepository(trips), [subscription])
            .ExecuteAsync(UserId, draft.Id);

        Assert.Equal(expectedStatus, result.Status);
        if (expectedStatus == FinalizeTripResultStatus.SavedTripQuotaExceeded)
        {
            Assert.Equal(3, result.Used);
            Assert.Equal(3, result.Limit);
        }
    }

    [Fact]
    public async Task Execute_ActiveMembership_HasUnlimitedFinalizedTrips()
    {
        var draft = Trip(TripStatus.Draft, UserId);
        var trips = Enumerable.Range(0, 5)
            .Select(_ => Trip(TripStatus.Finalized, UserId))
            .Append(draft)
            .ToArray();
        var subscription = Subscription(PlanCode.Membership, Now.AddDays(30));

        var result = await Command(new FakeTripRepository(trips), [subscription])
            .ExecuteAsync(UserId, draft.Id);

        Assert.Equal(FinalizeTripResultStatus.Success, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Execute_ExpiredPaidPlan_FallsBackToFree(int secondsFromNow)
    {
        var finalized = Trip(TripStatus.Finalized, UserId);
        var draft = Trip(TripStatus.Draft, UserId);
        var subscription = Subscription(PlanCode.Membership, Now.AddSeconds(secondsFromNow));

        var result = await Command(
                new FakeTripRepository([finalized, draft]),
                [subscription])
            .ExecuteAsync(UserId, draft.Id);

        Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, result.Status);
        Assert.Equal(1, result.Limit);
    }

    [Fact]
    public async Task Execute_SoftDeletedFinalizedTrip_DoesNotConsumeQuota()
    {
        var deleted = Trip(TripStatus.Finalized, UserId);
        deleted.DeletedAt = Now.AddDays(-1);
        var draft = Trip(TripStatus.Draft, UserId);

        var result = await Command(new FakeTripRepository([deleted, draft]))
            .ExecuteAsync(UserId, draft.Id);

        Assert.Equal(FinalizeTripResultStatus.Success, result.Status);
        Assert.Equal(TripStatus.Finalized, draft.Status);
    }

    [Fact]
    public async Task Execute_HistoricalOverLimit_BlocksNewFinalizeWithoutDeletingData()
    {
        var finalizedTrips = Enumerable.Range(0, 5)
            .Select(_ => Trip(TripStatus.Finalized, UserId))
            .ToArray();
        var draft = Trip(TripStatus.Draft, UserId);
        var allTrips = finalizedTrips.Append(draft).ToArray();
        var repository = new FakeTripRepository(allTrips);

        var result = await Command(repository).ExecuteAsync(UserId, draft.Id);

        Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, result.Status);
        Assert.Equal(5, result.Used);
        Assert.Equal(1, result.Limit);
        Assert.Equal(5, allTrips.Count(trip => trip.Status == TripStatus.Finalized));
        Assert.Equal(TripStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task Execute_MissingPersistedUser_PreservesNotFoundBehavior()
    {
        var trip = Trip(TripStatus.Draft, UserId);
        var executor = new FakeQuotaExecutor(persistedUserExists: false);

        var result = await Command(new FakeTripRepository([trip]), executor: executor)
            .ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
    }

    [Fact]
    public async Task Execute_Success_EnqueuesItineraryEmail()
    {
        var trip = Trip(TripStatus.Draft, UserId);
        var outbox = new FakeEmailOutboxRepository();

        await Command(new FakeTripRepository([trip]), outbox: outbox).ExecuteAsync(UserId, trip.Id);

        var (entry, now) = Assert.Single(outbox.Entries);
        Assert.Equal(Now, now);
        Assert.Equal("an@example.com", entry.ToEmail);
        Assert.Equal(EmailTemplateNames.TripItinerary, entry.TemplateName);
        Assert.Equal($"trip-itinerary:{trip.Id}", entry.DeduplicationKey);
        Assert.Equal("Lịch trình chuyến đi của bạn - LocalMate AI", entry.Subject);
    }

    [Fact]
    public async Task Execute_Rejected_DoesNotEnqueueItineraryEmail()
    {
        var outbox = new FakeEmailOutboxRepository();
        var alreadyFinalized = Trip(TripStatus.Finalized, UserId);
        var lostRace = Trip(TripStatus.Draft, UserId);
        var overQuota = Trip(TripStatus.Draft, UserId);

        await Command(new FakeTripRepository([alreadyFinalized]), outbox: outbox)
            .ExecuteAsync(UserId, alreadyFinalized.Id);
        await Command(new FakeTripRepository([lostRace], finalizeResult: false), outbox: outbox)
            .ExecuteAsync(UserId, lostRace.Id);
        await Command(new FakeTripRepository([Trip(TripStatus.Finalized, UserId), overQuota]), outbox: outbox)
            .ExecuteAsync(UserId, overQuota.Id);

        Assert.Empty(outbox.Entries);
    }

    private static FinalizeTripCommand Command(
        FakeTripRepository tripRepository,
        IReadOnlyList<UserSubscription>? subscriptions = null,
        FakeQuotaExecutor? executor = null,
        FakeEmailOutboxRepository? outbox = null) =>
        new(
            tripRepository,
            new FakeSubscriptionRepository(subscriptions ?? []),
            executor ?? new FakeQuotaExecutor(),
            new FakeUserRepository(new User { Id = UserId, FullName = "Nguyễn An", Email = "an@example.com" }),
            outbox ?? new FakeEmailOutboxRepository(),
            new FixedTimeProvider(Now));

    private static Trip Trip(TripStatus status, Guid userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StartLatitude = 10.77,
            StartLongitude = 106.69,
            DurationHours = 3,
            BudgetMin = 0,
            BudgetMax = 300_000,
            Status = status
        };

    private static UserSubscription Subscription(PlanCode planCode, DateTime endsAt) =>
        new()
        {
            UserId = UserId,
            PlanCode = planCode,
            StartsAt = Now.AddDays(-1),
            EndsAt = endsAt
        };

    private sealed class FakeQuotaExecutor(bool persistedUserExists = true) : ITripFinalizeQuotaExecutor
    {
        public int ExecutionCount { get; private set; }

        public async Task<TripFinalizeQuotaExecution<T>> ExecuteForUserAsync<T>(
            Guid userId,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            if (!persistedUserExists)
            {
                return new TripFinalizeQuotaExecution<T>(false, default);
            }

            return new TripFinalizeQuotaExecution<T>(true, await operation(cancellationToken));
        }
    }

    private sealed class FakeSubscriptionRepository(IReadOnlyList<UserSubscription> subscriptions)
        : TestSubscriptionRepository
    {
        public override Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(subscriptions);

        public override Task<UserSubscription?> GetByUserAndPlanAsync(
            Guid userId,
            PlanCode planCode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task AddAsync(
            UserSubscription subscription,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTripRepository(
        IReadOnlyList<Trip> trips,
        bool finalizeResult = true) : ITripRepository
    {
        public int FinalizeCalls { get; private set; }

        public Task<int> CountFinalizedByUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(trips.Count(trip => trip.UserId == userId
                                                && trip.Status == TripStatus.Finalized
                                                && trip.DeletedAt == null));

        public Task<Trip?> GetByIdAsync(
            Guid tripId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(trips.SingleOrDefault(trip => trip.Id == tripId));

        public Task<bool> FinalizeTripAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            FinalizeCalls++;
            var trip = trips.Single(candidate => candidate.Id == tripId);
            if (finalizeResult)
            {
                trip.Status = TripStatus.Finalized;
            }

            return Task.FromResult(finalizeResult);
        }

        public Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> AttachUserIfUnownedAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(
            Guid itemId,
            Guid userId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> MarkItineraryItemVisitedIfEligibleAsync(
            Guid itemId,
            Guid userId,
            DateTimeOffset visitedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Trip?> ForkTripAsync(
            Guid sourceTripId,
            Guid userId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TripDetailReadModel?> GetOwnedDetailAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(trips
                .Where(trip => trip.Id == tripId && trip.UserId == userId)
                .Select(trip => new TripDetailReadModel(
                    trip.Id, trip.Status, trip.StartLatitude, trip.StartLongitude, "Bến Thành",
                    trip.DurationHours, trip.BudgetMin, trip.BudgetMax, [], [],
                    Now, Now, Now, trip.TravelMode, trip.PlannedStartAt))
                .SingleOrDefault());

        public Task AddAsync(
            Trip trip,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> SoftDeleteAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakeUserRepository(User user) : IUserRepository
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == user.Id ? user : null);

        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeEmailOutboxRepository : IEmailOutboxRepository
    {
        public List<(EmailOutboxEntry Entry, DateTime Now)> Entries { get; } = [];

        public Task<bool> EnqueueAsync(EmailOutboxEntry entry, DateTime now, CancellationToken cancellationToken = default)
        {
            Entries.Add((entry, now));
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ClaimedEmailOutboxMessage>> ClaimDueAsync(
            DateTime now, DateTime leaseUntil, int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkSentAsync(Guid id, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ScheduleRetryAsync(Guid id, DateTime nextAttemptAt, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkFailedAsync(Guid id, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> DeleteExpiredAsync(DateTime sentBefore, DateTime failedBefore, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
