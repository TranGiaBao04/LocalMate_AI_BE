using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class SubscriptionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Plans_ReturnStableStringCodesAndCatalogValues()
    {
        var service = CreateService();

        var plans = service.GetPlans();

        Assert.Equal(["Free", "TripPass", "Membership"], plans.Select(plan => plan.Code));
        Assert.Equal(0, plans[0].Price);
        Assert.Equal(49_000, plans[1].Price);
        Assert.Equal(59_000, plans[2].Price);
    }

    [Fact]
    public async Task Me_FreeAccount_ReturnsDefaultLimitsAndVietnamReset()
    {
        var response = await CreateService().GetMySubscriptionAsync(UserId);

        Assert.NotNull(response);
        Assert.Equal("Free", response.Plan);
        Assert.Null(response.EndsAt);
        Assert.Equal(0, response.Usage.GenerateUsed);
        Assert.Equal(1, response.Usage.GenerateLimit);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), response.Usage.ResetAt);
        Assert.Equal(0, response.SavedTrips.Used);
        Assert.Equal(1, response.SavedTrips.Limit);
    }

    [Fact]
    public async Task Me_FreeAccount_CountsOnlyCurrentVietnamMonthGenerateEvents()
    {
        var events = new FakeUsageRepository(
        [Now.AddTicks(-1), Now, new DateTime(2026, 10, 31, 16, 59, 59, DateTimeKind.Utc),
            new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc)]);

        var response = await CreateService(events: events).GetMySubscriptionAsync(UserId);

        Assert.Equal(2, response!.Usage.GenerateUsed);
        Assert.Equal(UsageEventType.Generate, events.LastType);
        Assert.Equal(Now, events.LastStart);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), events.LastEnd);
    }

    [Fact]
    public async Task Me_TripPass_CountsOnlyNonDeletedFinalizedTrips()
    {
        var endsAt = Now.AddDays(7);
        var subscriptions = new FakeSubscriptionRepository(
            [new UserSubscription { UserId = UserId, PlanCode = PlanCode.TripPass, EndsAt = endsAt }]);
        var trips = new FakeTripRepository(
        [
            Trip(UserId, TripStatus.Finalized),
            Trip(UserId, TripStatus.Finalized),
            Trip(UserId, TripStatus.Draft),
            Trip(UserId, TripStatus.Finalized, deleted: true),
            Trip(Guid.NewGuid(), TripStatus.Finalized)
        ]);

        var response = await CreateService(subscriptions: subscriptions, trips: trips)
            .GetMySubscriptionAsync(UserId);

        Assert.Equal("TripPass", response!.Plan);
        Assert.Equal(endsAt, response.EndsAt);
        Assert.Null(response.Usage.GenerateLimit);
        Assert.Equal(2, response.SavedTrips.Used);
        Assert.Equal(3, response.SavedTrips.Limit);
    }

    [Fact]
    public async Task Me_MembershipWinsAndHasUnlimitedLimits()
    {
        var subscriptions = new FakeSubscriptionRepository(
        [
            new UserSubscription { UserId = UserId, PlanCode = PlanCode.TripPass, EndsAt = Now.AddDays(7) },
            new UserSubscription { UserId = UserId, PlanCode = PlanCode.Membership, EndsAt = Now.AddDays(30) }
        ]);
        var trips = new FakeTripRepository(Enumerable.Range(0, 10)
            .Select(_ => Trip(UserId, TripStatus.Finalized)).ToArray());

        var response = await CreateService(subscriptions: subscriptions, trips: trips)
            .GetMySubscriptionAsync(UserId);

        Assert.Equal("Membership", response!.Plan);
        Assert.Equal(Now.AddDays(30), response.EndsAt);
        Assert.Null(response.Usage.GenerateLimit);
        Assert.Equal(10, response.SavedTrips.Used);
        Assert.Null(response.SavedTrips.Limit);
    }

    [Fact]
    public async Task Me_LegacyFreeUserWithFiveFinalizedTrips_PreservesVisibleCount()
    {
        var trips = new FakeTripRepository(Enumerable.Range(0, 5)
            .Select(_ => Trip(UserId, TripStatus.Finalized)).ToArray());

        var response = await CreateService(trips: trips).GetMySubscriptionAsync(UserId);

        Assert.Equal("Free", response!.Plan);
        Assert.Equal(5, response.SavedTrips.Used);
        Assert.Equal(1, response.SavedTrips.Limit);
    }

    [Fact]
    public async Task Me_NonPersistedIdentity_DoesNotQuerySubscriptionData()
    {
        var subscriptions = new FakeSubscriptionRepository([]);
        var events = new FakeUsageRepository([]);
        var trips = new FakeTripRepository([]);

        var response = await CreateService(
            persistedUser: false, subscriptions: subscriptions, events: events, trips: trips)
            .GetMySubscriptionAsync(UserId);

        Assert.Null(response);
        Assert.Equal(0, subscriptions.Calls);
        Assert.Equal(0, events.Calls);
        Assert.Equal(0, trips.Calls);
    }

    [Fact]
    public async Task Me_PersistedAdmin_IsAllowed()
    {
        var response = await CreateService(role: UserRole.Admin).GetMySubscriptionAsync(UserId);

        Assert.Equal("Free", response!.Plan);
    }

    private static Trip Trip(Guid userId, TripStatus status, bool deleted = false) =>
        new() { UserId = userId, Status = status, DeletedAt = deleted ? Now : null };

    private static SubscriptionService CreateService(
        bool persistedUser = true,
        UserRole role = UserRole.User,
        FakeSubscriptionRepository? subscriptions = null,
        FakeUsageRepository? events = null,
        FakeTripRepository? trips = null) =>
        new(
            new FakeUserRepository(persistedUser ? new User { Id = UserId, Role = role } : null),
            subscriptions ?? new FakeSubscriptionRepository([]),
            events ?? new FakeUsageRepository([]),
            trips ?? new FakeTripRepository([]),
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakeSubscriptionRepository(IReadOnlyList<UserSubscription> subscriptions)
        : ISubscriptionRepository
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<UserSubscription>>(
                subscriptions.Where(subscription => subscription.UserId == userId).ToArray());
        }
    }

    private sealed class FakeUsageRepository(IReadOnlyList<DateTime> events) : IUsageEventRepository
    {
        public int Calls { get; private set; }
        public UsageEventType? LastType { get; private set; }
        public DateTime LastStart { get; private set; }
        public DateTime LastEnd { get; private set; }

        public Task<int> CountAsync(
            Guid userId, UsageEventType type, DateTime startUtc, DateTime nextStartUtc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastType = type;
            LastStart = startUtc;
            LastEnd = nextStartUtc;
            return Task.FromResult(events.Count(date => date >= startUtc && date < nextStartUtc));
        }
    }

    private sealed class FakeTripRepository(IReadOnlyList<Trip> trips) : ITripRepository
    {
        public int Calls { get; private set; }

        public Task<int> CountFinalizedByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(trips.Count(trip => trip.UserId == userId
                && trip.Status == TripStatus.Finalized && trip.DeletedAt == null));
        }

        public Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AttachUserIfUnownedAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> FinalizeTripAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(Guid itemId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> MarkItineraryItemVisitedIfEligibleAsync(Guid itemId, Guid userId, DateTimeOffset visitedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Trip?> ForkTripAsync(Guid sourceTripId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TripDetailReadModel?> GetOwnedDetailAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Trip trip, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeUserRepository(User? user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Id == userId ? user : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
