using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class SignupTrialServiceTests
{
    // 10:00 giờ VN ngày 20/10/2026.
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task RunningProgram_GrantsTripPassFromNow_AndNotifies()
    {
        var f = new Fixture();
        var program = f.AddProgram(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 31), trialDays: 7, max: 100, granted: 37);

        Assert.Equal(SignupTrialOutcome.Granted, await f.Service.GrantAsync(UserId));

        var grant = Assert.Single(f.Grants.Added);
        Assert.Equal((UserId, SubscriptionBaseline.PlanId(PlanCode.TripPass), SubscriptionBaseline.VersionId(PlanCode.TripPass)),
            (grant.UserId, grant.PlanId, grant.PlanVersionId));
        Assert.Equal((Now.UtcDateTime, Now.UtcDateTime.AddDays(7), 7), (grant.StartsAt, grant.EndsAt, grant.Days));
        Assert.Equal((PlanGrantSource.SignupTrial, (Guid?)program.Id, $"signup-trial:{UserId}"),
            (grant.Source, grant.TrialProgramId, grant.DeduplicationKey));
        Assert.Null(grant.Reason);
        Assert.Null(grant.GrantedByUserId);
        var (entry, _) = Assert.Single(f.Notifications.Entries);
        Assert.Equal(NotificationBuilder.SignupTrialGranted(UserId, grant.Id, "Trip Pass", grant.EndsAt), entry);
        Assert.Equal([AdminLocks.TrialPrograms], f.Executor.LockNames);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("disabled")]
    [InlineData("not-started")]
    [InlineData("ended")]
    public async Task NoRunningProgram_DoesNothing_WithoutTakingTheLock(string situation)
    {
        var f = new Fixture();
        switch (situation)
        {
            case "disabled": f.AddProgram(new(2026, 10, 15), new(2026, 10, 31), enabled: false); break;
            case "not-started": f.AddProgram(new(2026, 10, 21), new(2026, 10, 31)); break;
            case "ended": f.AddProgram(new(2026, 10, 1), new(2026, 10, 19)); break;
        }

        Assert.Equal(SignupTrialOutcome.NoProgram, await f.Service.GrantAsync(UserId));
        Assert.Empty(f.Executor.LockNames);
        Assert.Empty(f.Grants.Added);
    }

    [Theory]
    [InlineData(16, 50, SignupTrialOutcome.Granted)]  // 23:50 giờ VN ngày cuối
    [InlineData(17, 0, SignupTrialOutcome.NoProgram)] // 00:00 giờ VN hôm sau
    public async Task LastDay_IsDecidedByTheVietnamDate(int utcHour, int utcMinute, SignupTrialOutcome expected)
    {
        var f = new Fixture();
        f.Clock.Now = new DateTimeOffset(2026, 10, 31, utcHour, utcMinute, 0, TimeSpan.Zero);
        f.AddProgram(new(2026, 10, 15), new(2026, 10, 31));

        Assert.Equal(expected, await f.Service.GrantAsync(UserId));
    }

    [Fact]
    public async Task FullProgram_GrantsNothing()
    {
        var f = new Fixture();
        f.AddProgram(new(2026, 10, 15), new(2026, 10, 31), max: 100, granted: 100);

        Assert.Equal(SignupTrialOutcome.Full, await f.Service.GrantAsync(UserId));
        Assert.Empty(f.Grants.Added);
        Assert.Empty(f.Notifications.Entries);
    }

    [Fact]
    public async Task ProgramDisabledWhileWaitingForTheLock_GrantsNothing()
    {
        var f = new Fixture();
        var program = f.AddProgram(new(2026, 10, 15), new(2026, 10, 31));
        f.Executor.BeforeOperation = () => { f.Programs.Remove(program); };

        Assert.Equal(SignupTrialOutcome.NoProgram, await f.Service.GrantAsync(UserId));
        Assert.Empty(f.Grants.Added);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("no-version")]
    public async Task TripPassUnavailable_GrantsNothing(string situation)
    {
        var f = new Fixture();
        f.AddProgram(new(2026, 10, 15), new(2026, 10, 31));
        var tripPass = f.Subscriptions.Plans.Single(p => p.Code == "TRIP_PASS");
        if (situation == "inactive")
        {
            tripPass.IsActive = false;
        }
        else
        {
            tripPass.CurrentVersionId = null;
        }

        Assert.Equal(SignupTrialOutcome.PlanUnavailable, await f.Service.GrantAsync(UserId));
        Assert.Empty(f.Grants.Added);
    }

    [Fact]
    public async Task SecondCallForTheSameUser_IsIgnored_WithoutASecondNotification()
    {
        var f = new Fixture();
        f.AddProgram(new(2026, 10, 15), new(2026, 10, 31));
        await f.Service.GrantAsync(UserId);

        Assert.Equal(SignupTrialOutcome.AlreadyGranted, await f.Service.GrantAsync(UserId));
        Assert.Single(f.Grants.Added);
        Assert.Single(f.Notifications.Entries);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Service = new SignupTrialService(new FakeProgramRepository(Programs), Grants, Subscriptions, Executor,
                Notifications, Clock, NullLogger<SignupTrialService>.Instance);
        }

        public List<TrialProgramReadModel> Programs { get; } = [];
        public FakeGrantRepository Grants { get; } = new();
        public MemorySubscriptions Subscriptions { get; } = new();
        public RecordingExecutor Executor { get; } = new();
        public RecordingNotificationRepository Notifications { get; } = new();
        public MutableClock Clock { get; } = new();
        public SignupTrialService Service { get; }

        public TrialProgramReadModel AddProgram(DateOnly startsOn, DateOnly endsOn, int trialDays = 7, int? max = null,
            int granted = 0, bool enabled = true)
        {
            var program = new TrialProgramReadModel(Guid.NewGuid(), "Khai trương", startsOn, endsOn, trialDays, max,
                enabled, granted, Now.UtcDateTime, Now.UtcDateTime);
            Programs.Add(program);
            return program;
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = SignupTrialServiceTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingExecutor : IAdminOperationExecutor
    {
        public List<string> LockNames { get; } = [];
        public Action? BeforeOperation { get; set; }

        public Task<T> ExecuteExclusiveAsync<T>(string lockName, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            LockNames.Add(lockName);
            BeforeOperation?.Invoke();
            return operation(cancellationToken);
        }
    }

    private sealed class FakeProgramRepository(List<TrialProgramReadModel> programs) : ITrialProgramRepository
    {
        public Task<TrialProgramReadModel?> GetEnabledForDateAsync(DateOnly date, CancellationToken ct = default) =>
            Task.FromResult(programs.FirstOrDefault(p => p.IsEnabled && p.StartsOn <= date && date <= p.EndsOn));

        public Task<IReadOnlyList<TrialProgramReadModel>> GetAllAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TrialProgramReadModel?> GetAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> HasEnabledOverlapAsync(DateOnly startsOn, DateOnly endsOn, Guid? exceptId,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task AddAsync(TrialProgram program, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> UpdateAsync(Guid id, TrialProgramDraft draft, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGrantRepository : IPlanGrantRepository
    {
        public List<PlanGrant> Added { get; } = [];

        public Task<bool> TryAddOnceAsync(PlanGrant grant, DateTime nowUtc, CancellationToken ct = default)
        {
            if (Added.Any(g => g.DeduplicationKey == grant.DeduplicationKey))
            {
                return Task.FromResult(false);
            }

            Added.Add(grant);
            return Task.FromResult(true);
        }

        public Task<UserStatus?> GetUserStatusAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(PlanGrant grant, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<PlanGrantResponse?> GetAsync(Guid grantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlanGrantResponse>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemorySubscriptions : TestSubscriptionRepository
    {
        public override Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UserSubscription>>([]);
        public override Task<UserSubscription?> GetByUserAndPlanAsync(Guid userId, PlanCode code, CancellationToken ct = default) =>
            Task.FromResult<UserSubscription?>(null);
        public override Task AddAsync(UserSubscription s, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
