using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class AiUsageGuardTests
{
    // 2026-10-05 10:30 UTC = 17:30 ngày 05/10 giờ Việt Nam ⇒ ngày Việt Nam bắt đầu lúc 2026-10-04 17:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 30, 0, TimeSpan.Zero);
    private static readonly DateTime DayStartUtc = new(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();

    [Fact]
    public async Task Check_UnderEveryLimit_IsAllowed_AndCountsFromVietnamMidnight()
    {
        var fixture = new Fixture { Repository = { UserCalls = 9, TripCalls = 2 } };

        var decision = await fixture.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId);

        Assert.Equal(new AiUsageDecision(AiUsageStatus.Allowed), decision);
        Assert.Equal((UserId, DayStartUtc), Assert.Single(fixture.Repository.UserCountRequests));
        Assert.Equal((TripId, LlmCallKind.Explain), Assert.Single(fixture.Repository.TripCountRequests));
    }

    [Fact]
    public async Task Check_DisabledBySetting_SkipsCounting()
    {
        var fixture = new Fixture();
        fixture.Settings.Set(SystemSettingKeys.AiEnabled, 0);

        var decision = await fixture.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId);

        Assert.Equal(AiUsageStatus.Disabled, decision.Status);
        Assert.Empty(fixture.Repository.UserCountRequests);
        Assert.Empty(fixture.Repository.TripCountRequests);
        Assert.Equal(0, fixture.Subscriptions.Reads);
    }

    [Fact]
    public async Task Check_ClientNotConfigured_IsDisabled_WithoutReadingSettings()
    {
        var fixture = new Fixture { Client = { IsConfigured = false } };

        var decision = await fixture.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest);

        Assert.Equal(AiUsageStatus.Disabled, decision.Status);
        Assert.Empty(fixture.Settings.RequestedKeys);
        Assert.Equal(0, fixture.Subscriptions.Reads);
    }

    [Fact]
    public async Task Check_DailyLimitReached_ReportsNextVietnamMidnight()
    {
        var fixture = new Fixture { Repository = { UserCalls = 10 } };

        var decision = await fixture.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest);

        Assert.Equal(new AiUsageDecision(AiUsageStatus.DailyLimitReached, DayStartUtc.AddDays(1)), decision);
        Assert.Equal(DateTimeKind.Utc, decision.ResetAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task Check_TripLimitReached_WinsOverDailyLimit()
    {
        var fixture = new Fixture { Repository = { UserCalls = 10, TripCalls = 3 } };

        var decision = await fixture.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId);

        Assert.Equal(new AiUsageDecision(AiUsageStatus.TripLimitReached), decision);
        Assert.Empty(fixture.Repository.UserCountRequests);
    }

    [Fact]
    public async Task Check_ParseRequest_NeverCountsPerTrip()
    {
        var fixture = new Fixture { Repository = { TripCalls = 99 } };

        var decision = await fixture.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest, TripId);

        Assert.Equal(AiUsageStatus.Allowed, decision.Status);
        Assert.Empty(fixture.Repository.TripCountRequests);
    }

    [Fact]
    public async Task Check_LimitsComeFromSettings()
    {
        var fixture = new Fixture { Repository = { UserCalls = 5, TripCalls = 1 } };
        fixture.Settings.Set(SystemSettingKeys.AiDailyCallsPerUser, 5);
        fixture.Settings.Set(SystemSettingKeys.AiExplainCallsPerTrip, 1);

        Assert.Equal(
            AiUsageStatus.TripLimitReached,
            (await fixture.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
        Assert.Equal(
            AiUsageStatus.DailyLimitReached,
            (await fixture.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
    }

    [Fact]
    public async Task Record_StoresModelTokensDurationAndOutcome()
    {
        var fixture = new Fixture();

        await fixture.Guard.RecordAsync(
            UserId, LlmCallKind.Explain, TripId, LlmCallOutcome.Succeeded, new LlmJsonResponse("{}", 1100, 420), 2300);

        var log = Assert.Single(fixture.Repository.Added);
        Assert.Equal(UserId, log.UserId);
        Assert.Equal(TripId, log.TripId);
        Assert.Equal(LlmCallKind.Explain, log.Kind);
        Assert.Equal("model-x", log.Model);
        Assert.Equal(1100, log.InputTokens);
        Assert.Equal(420, log.OutputTokens);
        Assert.Equal(2300, log.DurationMilliseconds);
        Assert.Equal(LlmCallOutcome.Succeeded, log.Outcome);
    }

    [Fact]
    public async Task Record_FailedCallWithoutResponse_StoresZeroTokens()
    {
        var fixture = new Fixture();

        await fixture.Guard.RecordAsync(UserId, LlmCallKind.ParseRequest, null, LlmCallOutcome.ProviderFailed, null, 10_000);

        var log = Assert.Single(fixture.Repository.Added);
        Assert.Null(log.TripId);
        Assert.Equal(0, log.InputTokens);
        Assert.Equal(0, log.OutputTokens);
        Assert.Equal(LlmCallOutcome.ProviderFailed, log.Outcome);
    }

    [Theory]
    // 16:59 UTC ngày 04 = 23:59 ngày 04 giờ VN ⇒ ngày VN bắt đầu 17:00 UTC ngày 03.
    [InlineData(4, 16, 59, 3)]
    // 17:00 UTC ngày 04 = 00:00 ngày 05 giờ VN ⇒ ngày VN bắt đầu đúng lúc đó.
    [InlineData(4, 17, 0, 4)]
    public void StartOfTodayUtc_FollowsVietnamCalendarDay(int day, int hour, int minute, int expectedStartDay)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.Zero));

        Assert.Equal(
            new DateTime(2026, 10, expectedStartDay, 17, 0, 0, DateTimeKind.Utc),
            VietnamTime.StartOfTodayUtc(clock));
    }

    [Theory]
    [InlineData(PlanCode.Free, 3, 1)]
    [InlineData(PlanCode.TripPass, 15, 3)]
    [InlineData(PlanCode.Membership, 30, 3)]
    public async Task CanonicalVersion_UsesExactDailyAndExplainTerms(PlanCode code, int daily, int explain)
    {
        var f = new Fixture();
        f.SetFreeTerms(3, 1);
        if (code != PlanCode.Free)
            f.Paid(code, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(1));
        f.Repository.UserCalls = daily - 1;
        f.Repository.TripCalls = explain - 1;
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
        f.Repository.UserCalls = daily;
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        f.Repository.TripCalls = explain;
        Assert.Equal(AiUsageStatus.TripLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
        Assert.DoesNotContain(SystemSettingKeys.AiDailyCallsPerUser, f.Settings.RequestedKeys);
        Assert.DoesNotContain(SystemSettingKeys.AiExplainCallsPerTrip, f.Settings.RequestedKeys);
    }

    [Fact]
    public async Task Membership_IsNotCappedByLegacyTen()
    {
        var f = new Fixture { Repository = { UserCalls = 29 } };
        f.Paid(PlanCode.Membership, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(1));
        f.Settings.Set(SystemSettingKeys.AiDailyCallsPerUser, 10);
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        Assert.DoesNotContain(SystemSettingKeys.AiDailyCallsPerUser, f.Settings.RequestedKeys);
    }

    [Theory]
    [InlineData(null, 1, 7, 1, AiUsageStatus.DailyLimitReached)]
    [InlineData(7, null, 7, 2, AiUsageStatus.TripLimitReached)]
    [InlineData(0, 1, 0, 0, AiUsageStatus.DailyLimitReached)]
    [InlineData(7, 0, 0, 0, AiUsageStatus.TripLimitReached)]
    public async Task EachField_FallsBackOnlyForNull(int? daily, int? explain, int used, int tripUsed,
        AiUsageStatus expected)
    {
        var f = new Fixture();
        f.SetFreeTerms(daily, explain);
        f.Settings.Set(SystemSettingKeys.AiDailyCallsPerUser, 7);
        f.Settings.Set(SystemSettingKeys.AiExplainCallsPerTrip, 2);
        f.Repository.UserCalls = used;
        f.Repository.TripCalls = tripUsed;
        var kind = expected == AiUsageStatus.TripLimitReached ? LlmCallKind.Explain : LlmCallKind.ParseRequest;
        Assert.Equal(expected, (await f.Guard.CheckAsync(UserId, kind, TripId)).Status);
        Assert.Equal(daily is null && kind == LlmCallKind.ParseRequest,
            f.Settings.RequestedKeys.Contains(SystemSettingKeys.AiDailyCallsPerUser));
        Assert.Equal(explain is null && kind == LlmCallKind.Explain,
            f.Settings.RequestedKeys.Contains(SystemSettingKeys.AiExplainCallsPerTrip));
    }

    [Theory]
    [InlineData("missing-paid-version")]
    [InlineData("wrong-paid-binding")]
    [InlineData("missing-free")]
    [InlineData("missing-free-version")]
    [InlineData("wrong-free-binding")]
    [InlineData("inactive-free")]
    [InlineData("repository-error")]
    public async Task ResolverFailure_IsVisibleWithoutFallbackOrCounting(string corruption)
    {
        var f = new Fixture();
        var free = f.Subscriptions.Plans.Single(p => p.Code == "FREE");
        switch (corruption)
        {
            case "missing-paid-version":
                f.Paid(PlanCode.Membership, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(1), Guid.NewGuid());
                break;
            case "wrong-paid-binding":
                f.Paid(PlanCode.Membership, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(1),
                    free.CurrentVersionId);
                break;
            case "missing-free": f.Subscriptions.Plans.Remove(free); break;
            case "missing-free-version": free.CurrentVersionId = Guid.NewGuid(); break;
            case "wrong-free-binding": free.CurrentVersionId = SubscriptionBaseline.VersionId(PlanCode.Membership); break;
            case "inactive-free": free.IsActive = false; break;
            case "repository-error": f.Subscriptions.Failure = new InvalidOperationException("DB unavailable"); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId));
        Assert.DoesNotContain(SystemSettingKeys.AiDailyCallsPerUser, f.Settings.RequestedKeys);
        Assert.DoesNotContain(SystemSettingKeys.AiExplainCallsPerTrip, f.Settings.RequestedKeys);
        Assert.Empty(f.Repository.UserCountRequests);
        Assert.Empty(f.Repository.TripCountRequests);
    }

    [Fact]
    public async Task SameDayUpgrade_RaisesAllowanceWithoutResettingUserUsage()
    {
        var f = new Fixture { Repository = { UserCalls = 3 } };
        f.SetFreeTerms(3, 1);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        f.Paid(PlanCode.Membership, Now.UtcDateTime, Now.UtcDateTime.AddDays(30));
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        Assert.Equal(3, f.Repository.UserCalls);
        Assert.All(f.Repository.UserCountRequests, request => Assert.Equal((UserId, DayStartUtc), request));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryOrTermination_ReturnsFreeWithoutResettingUsage(bool terminated)
    {
        var f = new Fixture { Repository = { UserCalls = 10 } };
        f.SetFreeTerms(3, 1);
        var period = f.Paid(PlanCode.Membership, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(1));
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        if (terminated) period.Terminate(Now.UtcDateTime, Guid.NewGuid());
        else f.Clock.Now = new DateTimeOffset(period.EndsAt);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        Assert.Equal(10, f.Repository.UserCalls);
    }

    [Fact]
    public async Task FuturePaid_DoesNotGrantEarly()
    {
        var f = new Fixture { Repository = { UserCalls = 3 } };
        f.SetFreeTerms(3, 1);
        f.Paid(PlanCode.Membership, Now.UtcDateTime.AddHours(1), Now.UtcDateTime.AddDays(30));
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
    }

    [Fact]
    public async Task PurchasedVersionAndQueuedRenewal_DoNotUseCurrentVersionOrResetUsage()
    {
        var f = new Fixture { Repository = { UserCalls = 15 } };
        var end = Now.UtcDateTime.AddHours(1);
        f.Paid(PlanCode.TripPass, Now.UtcDateTime.AddDays(-1), end);
        var next = new SubscriptionPlanVersion
        {
            PlanId = SubscriptionBaseline.PlanId(PlanCode.TripPass), VersionNumber = 2,
            AiDailyCallLimit = 20, AiExplainCallsPerTripLimit = 2
        };
        await f.Subscriptions.PublishVersionAsync(next);
        f.Paid(PlanCode.TripPass, end, end.AddDays(7), next.Id);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        f.Clock.Now = new DateTimeOffset(end);
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        Assert.Equal(15, f.Repository.UserCalls);
        Assert.All(f.Repository.UserCountRequests, request => Assert.Equal((UserId, DayStartUtc), request));
    }

    [Fact]
    public async Task ExplainAllowance_FollowsPlanWithoutResettingSuccessfulTripCount()
    {
        var f = new Fixture { Repository = { TripCalls = 2 } };
        f.SetFreeTerms(3, 1);
        var paid = f.Paid(PlanCode.Membership, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(1));
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
        paid.Terminate(Now.UtcDateTime, Guid.NewGuid());
        Assert.Equal(AiUsageStatus.TripLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
        f.Paid(PlanCode.Membership, Now.UtcDateTime, Now.UtcDateTime.AddDays(30));
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
        await f.Guard.RecordAsync(UserId, LlmCallKind.Explain, TripId, LlmCallOutcome.Succeeded, null, 1);
        Assert.Equal(AiUsageStatus.TripLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
    }

    [Theory]
    [InlineData(LlmCallOutcome.ProviderFailed)]
    [InlineData(LlmCallOutcome.InvalidOutput)]
    [InlineData(LlmCallOutcome.Succeeded)]
    public async Task EveryRecordedOutcome_CountsDaily_OnlySuccessfulExplainCountsTrip(LlmCallOutcome outcome)
    {
        var f = new Fixture();
        f.SetFreeTerms(3, 1);
        await f.Guard.RecordAsync(UserId, LlmCallKind.Explain, TripId, outcome, null, 1);
        Assert.Equal(outcome == LlmCallOutcome.Succeeded ? AiUsageStatus.TripLimitReached : AiUsageStatus.Allowed,
            (await f.Guard.CheckAsync(UserId, LlmCallKind.Explain, TripId)).Status);
        await f.Guard.RecordAsync(UserId, LlmCallKind.ParseRequest, null, outcome, null, 1);
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        await f.Guard.RecordAsync(UserId, LlmCallKind.ParseRequest, null, outcome, null, 1);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
    }

    [Fact]
    public async Task OneClockRead_AlignsEffectivePeriodAndVietnamMidnight()
    {
        var f = new Fixture { Repository = { UserCalls = 3 } };
        f.SetFreeTerms(3, 1);
        var midnight = new DateTimeOffset(2026, 10, 5, 17, 0, 0, TimeSpan.Zero);
        f.Paid(PlanCode.Membership, midnight.UtcDateTime, midnight.AddDays(30).UtcDateTime);
        f.Clock.Now = midnight.AddTicks(-1);
        Assert.Equal(new AiUsageDecision(AiUsageStatus.DailyLimitReached, midnight.UtcDateTime),
            await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest));
        Assert.Equal(1, f.Clock.Reads);
        Assert.Equal(DayStartUtc, f.Repository.UserCountRequests.Last().SinceUtc);
        f.Clock.Now = midnight;
        Assert.Equal(AiUsageStatus.Allowed, (await f.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest)).Status);
        Assert.Equal(2, f.Clock.Reads);
        Assert.Equal(midnight.UtcDateTime, f.Repository.UserCountRequests.Last().SinceUtc);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            SetFreeTerms(null, null);
            Guard = new AiUsageGuard(Client, Repository, Settings, Clock, Subscriptions);
        }

        public FakeClient Client { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeSystemSettingProvider Settings { get; } = new();
        public MemorySubscriptions Subscriptions { get; } = new();
        public FixedTimeProvider Clock { get; } = new(Now);
        public AiUsageGuard Guard { get; }

        public void SetFreeTerms(int? daily, int? explain)
        {
            var plan = Subscriptions.Plans.Single(p => p.Code == "FREE");
            var version = new SubscriptionPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 2,
                AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain
            };
            Subscriptions.Versions.Add(version);
            plan.CurrentVersionId = version.Id;
        }

        public SubscriptionPeriod Paid(PlanCode code, DateTime start, DateTime end, Guid? versionId = null)
        {
            var period = new SubscriptionPeriod
            {
                UserId = UserId, PlanId = SubscriptionBaseline.PlanId(code),
                PlanVersionId = versionId ?? SubscriptionBaseline.VersionId(code),
                StartsAt = start, EndsAt = end, SourcePaymentOrderId = Guid.NewGuid()
            };
            Subscriptions.Periods.Add(period);
            return period;
        }
    }

    private sealed class MemorySubscriptions : TestSubscriptionRepository
    {
        public int Reads { get; private set; }
        public Exception? Failure { get; set; }
        public override Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        {
            Reads++;
            if (Failure is { } error) throw error;
            return Task.FromResult<IReadOnlyList<UserSubscription>>([]);
        }
        public override Task<UserSubscription?> GetByUserAndPlanAsync(Guid userId, PlanCode code,
            CancellationToken ct = default) => throw new NotSupportedException();
        public override Task AddAsync(UserSubscription subscription, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeClient : ILlmClient
    {
        public string Model => "model-x";
        public bool IsConfigured { get; set; } = true;

        public Task<LlmJsonResponse> GenerateJsonAsync(
            LlmJsonRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRepository : ILlmCallLogRepository
    {
        public int UserCalls { get; set; }
        public int TripCalls { get; set; }
        public List<LlmCallLog> Added { get; } = [];
        public List<(Guid UserId, DateTime SinceUtc)> UserCountRequests { get; } = [];
        public List<(Guid TripId, LlmCallKind Kind)> TripCountRequests { get; } = [];

        public Task AddAsync(LlmCallLog log, CancellationToken cancellationToken = default)
        {
            Added.Add(log);
            return Task.CompletedTask;
        }

        public Task<int> CountForUserSinceAsync(
            Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default)
        {
            UserCountRequests.Add((userId, sinceUtc));
            return Task.FromResult(UserCalls + Added.Count(log => log.UserId == userId));
        }

        public Task<int> CountSucceededForTripAsync(
            Guid tripId, LlmCallKind kind, CancellationToken cancellationToken = default)
        {
            TripCountRequests.Add((tripId, kind));
            return Task.FromResult(TripCalls + Added.Count(log => log.TripId == tripId && log.Kind == kind
                && log.Outcome == LlmCallOutcome.Succeeded));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = utcNow;
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return Now;
        }
    }
}
