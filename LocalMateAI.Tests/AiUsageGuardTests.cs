using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

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
    }

    [Fact]
    public async Task Check_ClientNotConfigured_IsDisabled_WithoutReadingSettings()
    {
        var fixture = new Fixture { Client = { IsConfigured = false } };

        var decision = await fixture.Guard.CheckAsync(UserId, LlmCallKind.ParseRequest);

        Assert.Equal(AiUsageStatus.Disabled, decision.Status);
        Assert.Empty(fixture.Settings.RequestedKeys);
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

    private sealed class Fixture
    {
        public Fixture() => Guard = new AiUsageGuard(Client, Repository, Settings, new FixedTimeProvider(Now));

        public FakeClient Client { get; } = new();
        public FakeRepository Repository { get; } = new();
        public FakeSystemSettingProvider Settings { get; } = new();
        public AiUsageGuard Guard { get; }
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
            return Task.FromResult(UserCalls);
        }

        public Task<int> CountSucceededForTripAsync(
            Guid tripId, LlmCallKind kind, CancellationToken cancellationToken = default)
        {
            TripCountRequests.Add((tripId, kind));
            return Task.FromResult(TripCalls);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
