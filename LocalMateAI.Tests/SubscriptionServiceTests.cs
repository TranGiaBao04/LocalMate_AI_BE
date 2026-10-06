using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Application.Settings;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class SubscriptionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Plans_ReturnStableStringCodesAndCatalogValues()
    {
        var service = CreateService();

        var plans = await service.GetPlansAsync();

        Assert.Equal(["Free", "TripPass", "Membership"], plans.Select(plan => plan.Code));
        Assert.Equal(0, plans[0].Price);
        Assert.Equal(19_000, plans[1].Price);
        Assert.Equal(59_000, plans[2].Price);
        Assert.Equal(new (int?, int?)[] { (3, 1), (15, 3), (30, 3) },
            plans.Select(p => (p.AiDailyCallLimit, p.AiExplainCallsPerTripLimit)));
        Assert.All(plans, plan =>
        {
            var feature = Assert.Single(plan.Features);
            Assert.Equal("METRO_GOOGLE_MAPS", feature.Code);
            Assert.Equal("Bản đồ Metro & chỉ đường Google Maps", feature.Name);
        });
    }

    [Fact]
    public async Task Plans_UseCurrentVersionFeatures_WithoutChangingQuotaValues()
    {
        var repo = new FakeSubscriptionRepository([]);
        var plan = repo.Plans.Single(p => p.Code == "TRIP_PASS");
        var version = new SubscriptionPlanVersion
        {
            PlanId = plan.Id, VersionNumber = 2, Price = 19000, DurationDays = 7, SavedTripLimit = 3
        };
        await repo.PublishVersionAsync(version, []);
        var response = (await CreateService(subscriptions: repo).GetPlansAsync()).Single(p => p.Code == "TripPass");
        Assert.Empty(response.Features);
        Assert.Equal(19000, response.Price);
        Assert.Equal(7, response.DurationDays);
        Assert.Null(response.GenerateLimit);
        Assert.Equal(3, response.SavedTripLimit);
        Assert.Single(await repo.GetFeaturesForVersionAsync(repo.Versions.Single(v =>
            v.PlanId == plan.Id && v.VersionNumber == 1).Id));
    }

    [Fact]
    public void PlanResponse_ExistingPositionalConstructor_DefaultsToEmptyFeatures()
    {
        var response = new LocalMateAI.Application.DTOs.Subscription.SubscriptionPlanResponse("Free", 0, null, 1, 1);
        Assert.Empty(response.Features);
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
        Assert.Equal(new LocalMateAI.Application.DTOs.Subscription.SubscriptionAiResponse(0, 3, Now.AddDays(1)),
            response.Ai);
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
        Assert.Equal(15, response.Ai.DailyLimit);
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
        Assert.Equal(30, response.Ai.DailyLimit);
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
        var response = await CreateService(roleName: SystemRoles.AdminName).GetMySubscriptionAsync(UserId);

        Assert.Equal("Free", response!.Plan);
    }

    [Fact]
    public async Task Me_AiCountsEveryPersistedOutcomeAcrossKindsForOwnerAndVietnamDayOnly()
    {
        var rows = new List<LlmCallLog>();
        foreach (var kind in Enum.GetValues<LlmCallKind>())
            foreach (var outcome in Enum.GetValues<LlmCallOutcome>())
                rows.Add(new() { UserId = UserId, Kind = kind, Outcome = outcome, CreatedAt = Now, Model = "test" });
        rows.Add(new() { UserId = UserId, CreatedAt = Now.AddTicks(-1), Model = "test" });
        rows.Add(new() { UserId = Guid.NewGuid(), CreatedAt = Now, Model = "test" });
        var logs = new FakeLlmLogs(rows);
        var me = (await CreateService(logs: logs).GetMySubscriptionAsync(UserId))!;
        Assert.Equal(6, me.Ai.DailyUsed);
        Assert.Equal(3, me.Ai.DailyLimit);
        Assert.Equal((UserId, Now), Assert.Single(logs.Requests));
    }

    [Fact]
    public async Task Me_SameDayUpgradeChangesLimitWithoutResettingUsed()
    {
        var repo = new FakeSubscriptionRepository([]);
        var logs = Logs(3);
        var service = CreateService(subscriptions: repo, logs: logs);
        Assert.Equal(3, (await service.GetMySubscriptionAsync(UserId))!.Ai.DailyLimit);
        Paid(repo, PlanCode.Membership, Now, Now.AddDays(30));
        var me = (await service.GetMySubscriptionAsync(UserId))!;
        Assert.Equal("Membership", me.Plan);
        Assert.Equal(3, me.Ai.DailyUsed);
        Assert.Equal(30, me.Ai.DailyLimit);
        Assert.All(logs.Requests, request => Assert.Equal((UserId, Now), request));
    }

    [Fact]
    public async Task Me_ExpiredMembershipReportsTenUsedAndFreeLimitThreeWithoutClamping()
    {
        var repo = new FakeSubscriptionRepository([]);
        Paid(repo, PlanCode.Membership, Now.AddDays(-30), Now);
        var me = (await CreateService(subscriptions: repo, logs: Logs(10)).GetMySubscriptionAsync(UserId))!;
        Assert.Equal("Free", me.Plan);
        Assert.Equal(10, me.Ai.DailyUsed);
        Assert.Equal(3, me.Ai.DailyLimit);
    }

    [Fact]
    public async Task Me_FutureMembershipDoesNotGrantEarly()
    {
        var repo = new FakeSubscriptionRepository([]);
        Paid(repo, PlanCode.Membership, Now.AddTicks(1), Now.AddDays(30));
        var me = (await CreateService(subscriptions: repo).GetMySubscriptionAsync(UserId))!;
        Assert.Equal("Free", me.Plan);
        Assert.Equal(3, me.Ai.DailyLimit);
    }

    [Theory]
    [InlineData(null, 7)]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    public async Task Me_UsesSharedNullOnlyFallbackPolicy(int? daily, int expected)
    {
        var repo = new FakeSubscriptionRepository([]);
        var version = await Publish(repo, PlanCode.Free, daily, 1);
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.AiDailyCallsPerUser, 7);
        var me = (await CreateService(subscriptions: repo, settings: settings).GetMySubscriptionAsync(UserId))!;
        Assert.Equal(expected, me.Ai.DailyLimit);
        Assert.Equal(daily is null, settings.RequestedKeys.Contains(SystemSettingKeys.AiDailyCallsPerUser));
        Assert.Equal(version.Id, repo.Plans.Single(p => p.Code == "FREE").CurrentVersionId);
    }

    [Fact]
    public async Task Me_HistoricalCustomVersionWithNullAiUsesLegacySetting()
    {
        var repo = new FakeSubscriptionRepository([]);
        var custom = new SubscriptionPlan { Code = "CUSTOM_AI", Name = "Custom", EntitlementPriority = 400 };
        var version = new SubscriptionPlanVersion { PlanId = custom.Id, VersionNumber = 1 };
        custom.CurrentVersionId = version.Id;
        repo.Plans.Add(custom);
        repo.Versions.Add(version);
        repo.Periods.Add(new() { UserId = UserId, PlanId = custom.Id, PlanVersionId = version.Id,
            StartsAt = Now, EndsAt = Now.AddDays(1), SourcePaymentOrderId = Guid.NewGuid() });
        var me = (await CreateService(subscriptions: repo).GetMySubscriptionAsync(UserId))!;
        Assert.Equal("CUSTOM_AI", me.Plan);
        Assert.Equal(10, me.Ai.DailyLimit);
    }

    [Theory]
    [InlineData("missing-paid-version")]
    [InlineData("wrong-paid-binding")]
    [InlineData("missing-free-version")]
    [InlineData("repository-error")]
    public async Task Me_ResolverFailureDoesNotFabricateAiFallback(string corruption)
    {
        var repo = new FakeSubscriptionRepository([]);
        var free = repo.Plans.Single(p => p.Code == "FREE");
        if (corruption == "repository-error") repo.Failure = new InvalidOperationException("DB unavailable");
        else if (corruption == "missing-free-version") free.CurrentVersionId = Guid.NewGuid();
        else Paid(repo, PlanCode.Membership, Now, Now.AddDays(30),
            corruption == "wrong-paid-binding" ? free.CurrentVersionId : Guid.NewGuid());
        var settings = new FakeSystemSettingProvider();
        var logs = Logs(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(subscriptions: repo, settings: settings, logs: logs).GetMySubscriptionAsync(UserId));
        Assert.Empty(settings.RequestedKeys);
        Assert.Empty(logs.Requests);
    }

    [Fact]
    public async Task Me_CapturesOneClockAndResolvesOnceAroundVietnamMidnight()
    {
        var repo = new FakeSubscriptionRepository([]);
        Paid(repo, PlanCode.Membership, Now, Now.AddDays(30));
        var midnight = Now.AddDays(1);
        var clock = new AdvancingClock(midnight.AddTicks(-1));
        var logs = Logs(2);
        var me = (await CreateService(subscriptions: repo, logs: logs, clock: clock)
            .GetMySubscriptionAsync(UserId))!;
        Assert.Equal(1, clock.Reads);
        Assert.Equal(1, repo.Calls);
        Assert.Equal(30, me.Ai.DailyLimit);
        Assert.Equal(2, me.Ai.DailyUsed);
        Assert.Equal(midnight, me.Ai.ResetAt);
        Assert.Equal(DateTimeKind.Utc, me.Ai.ResetAt.Kind);
        Assert.Equal((UserId, Now), Assert.Single(logs.Requests));
    }

    [Fact]
    public async Task Me_GlobalAiDisabledDoesNotAlterEntitlementAndDoesNotReadProviderAvailability()
    {
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.AiEnabled, 0);
        var me = (await CreateService(logs: Logs(2), settings: settings).GetMySubscriptionAsync(UserId))!;
        Assert.Equal(2, me.Ai.DailyUsed);
        Assert.Equal(3, me.Ai.DailyLimit);
        Assert.Empty(settings.RequestedKeys);
    }

    [Fact]
    public async Task CatalogCurrentVersionChangesWhilePaidMeKeepsPurchasedAiVersion()
    {
        var repo = new FakeSubscriptionRepository([]);
        Paid(repo, PlanCode.Membership, Now, Now.AddDays(30));
        await Publish(repo, PlanCode.Membership, 42, 2);
        var service = CreateService(subscriptions: repo);
        var catalog = (await service.GetPlansAsync()).Single(p => p.Code == "Membership");
        Assert.Equal(42, catalog.AiDailyCallLimit);
        Assert.Equal(2, catalog.AiExplainCallsPerTripLimit);
        Assert.Equal(30, (await service.GetMySubscriptionAsync(UserId))!.Ai.DailyLimit);
    }

    private static FakeLlmLogs Logs(int count) => new(Enumerable.Range(0, count)
        .Select(_ => new LlmCallLog { UserId = UserId, CreatedAt = Now, Model = "test" }).ToArray());

    private static void Paid(FakeSubscriptionRepository repo, PlanCode code, DateTime start, DateTime end,
        Guid? versionId = null) => repo.Periods.Add(new()
        {
            UserId = UserId, PlanId = SubscriptionBaseline.PlanId(code),
            PlanVersionId = versionId ?? SubscriptionBaseline.VersionId(code),
            StartsAt = start, EndsAt = end, SourcePaymentOrderId = Guid.NewGuid()
        });

    private static async Task<SubscriptionPlanVersion> Publish(FakeSubscriptionRepository repo, PlanCode code,
        int? daily, int? explain)
    {
        var version = new SubscriptionPlanVersion
        {
            PlanId = SubscriptionBaseline.PlanId(code), VersionNumber = 2,
            AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain
        };
        await repo.PublishVersionAsync(version);
        return version;
    }

    private sealed class AdvancingClock(DateTime now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => new(now.AddTicks(Reads++));
    }

    private static Trip Trip(Guid userId, TripStatus status, bool deleted = false) =>
        new() { UserId = userId, Status = status, DeletedAt = deleted ? Now : null };

    private static SubscriptionService CreateService(
        bool persistedUser = true,
        string roleName = SystemRoles.UserName,
        FakeSubscriptionRepository? subscriptions = null,
        FakeUsageRepository? events = null,
        FakeTripRepository? trips = null,
        FakeLlmLogs? logs = null,
        FakeSystemSettingProvider? settings = null,
        TimeProvider? clock = null) =>
        new(
            new FakeUserRepository(persistedUser ? new User { Id = UserId, Role = new Role { Name = roleName } } : null),
            subscriptions ?? new FakeSubscriptionRepository([]),
            events ?? new FakeUsageRepository([]),
            trips ?? new FakeTripRepository([]),
            clock ?? new FixedTimeProvider(Now),
            logs ?? new FakeLlmLogs([]),
            settings ?? new FakeSystemSettingProvider());

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakeSubscriptionRepository(IReadOnlyList<UserSubscription> subscriptions)
        : TestSubscriptionRepository
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }

        public override Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Failure is { } error) throw error;
            return Task.FromResult<IReadOnlyList<UserSubscription>>(
                subscriptions.Where(subscription => subscription.UserId == userId).ToArray());
        }

        public override Task<UserSubscription?> GetByUserAndPlanAsync(
            Guid userId, PlanCode planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(subscriptions.SingleOrDefault(subscription =>
                subscription.UserId == userId && subscription.PlanCode == planCode));

        public override Task AddAsync(
            UserSubscription subscription,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLlmLogs(IReadOnlyList<LlmCallLog> logs) : IAiUsageAccountingRepository
    {
        public List<(Guid UserId, DateTime SinceUtc)> Requests { get; } = [];
        public Task<int> CountDailyAsync(Guid userId, DateOnly vietnamUsageDate, CancellationToken ct = default)
        {
            var sinceUtc = vietnamUsageDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7);
            Requests.Add((userId, sinceUtc));
            return Task.FromResult(logs.Count(l => l.UserId == userId && l.CreatedAt >= sinceUtc && l.CreatedAt < sinceUtc.AddDays(1)));
        }
    }

    private sealed class FakeUsageRepository(IReadOnlyList<DateTime> events) : IUsageEventRepository
    {
        public Task<int> CountForPeriodAsync(Guid userId, Guid periodId, UsageEventType type, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
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

        public Task AddAsync(UsageEvent usageEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
