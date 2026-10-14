using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class PlanGrantServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TripPassId = SubscriptionBaseline.PlanId(PlanCode.TripPass);

    [Fact]
    public async Task Grant_FreeUser_StartsNow_WritesGrantAndNotification()
    {
        var f = new Fixture();

        var result = await f.Service.GrantAsync(UserId, new(TripPassId, 7, "  Tặng khai trương  "), AdminId);

        Assert.Equal(PlanGrantResultStatus.Success, result.Status);
        var grant = Assert.Single(f.Subscriptions.Grants);
        Assert.Equal((UserId, TripPassId, SubscriptionBaseline.VersionId(PlanCode.TripPass)),
            (grant.UserId, grant.PlanId, grant.PlanVersionId));
        Assert.Equal((Now, Now.AddDays(7), 7), (grant.StartsAt, grant.EndsAt, grant.Days));
        Assert.Equal((PlanGrantSource.Admin, "Tặng khai trương", AdminId),
            (grant.Source, grant.Reason, grant.GrantedByUserId));
        Assert.Equal(grant.Id, result.Response!.GrantId);
        var (entry, at) = Assert.Single(f.Notifications.Entries);
        Assert.Equal(Now, at);
        Assert.Equal((NotificationTypes.PlanGranted, $"plan-grant:{grant.Id}"), (entry.Type, entry.DeduplicationKey));
        Assert.Equal("Gói Trip Pass có hiệu lực đến 21/10/2026.", entry.Body);
    }

    [Fact]
    public async Task Grant_Twice_ChainsTheSecondAfterTheFirst()
    {
        var f = new Fixture();
        await f.Service.GrantAsync(UserId, new(TripPassId, 7, "lần 1"), AdminId);

        var second = await f.Service.GrantAsync(UserId, new(TripPassId, 3, "lần 2"), AdminId);

        Assert.Equal(PlanGrantResultStatus.Success, second.Status);
        Assert.Equal((Now.AddDays(7), Now.AddDays(10)), (second.Response!.StartsAt, second.Response.EndsAt));
        Assert.Equal("Gói Trip Pass có hiệu lực từ 21/10/2026 đến 24/10/2026.", f.Notifications.Entries[1].Entry.Body);
    }

    [Fact]
    public async Task Grant_UnderPurchasedHigherPlan_StartsWhenThatPlanEnds()
    {
        var f = new Fixture();
        f.Subscriptions.Periods.Add(new SubscriptionPeriod
        {
            UserId = UserId,
            PlanId = SubscriptionBaseline.PlanId(PlanCode.Membership),
            PlanVersionId = SubscriptionBaseline.VersionId(PlanCode.Membership),
            StartsAt = Now.AddDays(-1),
            EndsAt = Now.AddDays(29),
            SourcePaymentOrderId = Guid.NewGuid()
        });

        var result = await f.Service.GrantAsync(UserId, new(TripPassId, 7, "test"), AdminId);

        Assert.Equal(Now.AddDays(29), result.Response!.StartsAt);
    }

    [Theory]
    [InlineData(0, "ok", "Days")]
    [InlineData(91, "ok", "Days")]
    [InlineData(7, null, "Reason")]
    [InlineData(7, "   ", "Reason")]
    public async Task Grant_InvalidBody_IsRejectedBeforeLockingTheUser(int days, string? reason, string field)
    {
        var f = new Fixture();

        var result = await f.Service.GrantAsync(UserId, new(TripPassId, days, reason), AdminId);

        Assert.Equal(PlanGrantResultStatus.InvalidRequest, result.Status);
        Assert.Equal([field], result.ValidationErrors!.Keys);
        Assert.Equal(0, f.Lock.Calls);
    }

    [Fact]
    public async Task Grant_ReasonLongerThanLimit_IsRejected()
    {
        var result = await new Fixture().Service.GrantAsync(
            UserId, new(TripPassId, 7, new string('a', PlanGrantRules.MaxReasonLength + 1)), AdminId);
        Assert.Equal(["Reason"], result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Grant_UnknownUser_ReturnsNotFound()
    {
        var f = new Fixture { UserStatus = null };
        var result = await f.Service.GrantAsync(UserId, new(TripPassId, 7, "test"), AdminId);
        Assert.Equal(PlanGrantResultStatus.UserNotFound, result.Status);
        Assert.Empty(f.Subscriptions.Grants);
    }

    [Fact]
    public async Task Grant_LockedUser_IsRejectedWithoutWriting()
    {
        var f = new Fixture { UserStatus = UserStatus.Locked };
        var result = await f.Service.GrantAsync(UserId, new(TripPassId, 7, "test"), AdminId);
        Assert.Equal(PlanGrantResultStatus.UserLocked, result.Status);
        Assert.Empty(f.Subscriptions.Grants);
        Assert.Empty(f.Notifications.Entries);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("inactive")]
    [InlineData("unknown")]
    [InlineData("no-version")]
    public async Task Grant_PlanThatCannotBeGranted_FailsOnPlanId(string situation)
    {
        var f = new Fixture();
        var tripPass = f.Subscriptions.Plans.Single(p => p.Id == TripPassId);
        var planId = TripPassId;
        switch (situation)
        {
            case "free": planId = SubscriptionBaseline.PlanId(PlanCode.Free); break;
            case "inactive": tripPass.IsActive = false; break;
            case "unknown": planId = Guid.NewGuid(); break;
            default: tripPass.CurrentVersionId = null; break;
        }

        var result = await f.Service.GrantAsync(UserId, new(planId, 7, "test"), AdminId);

        Assert.Equal(PlanGrantResultStatus.InvalidRequest, result.Status);
        Assert.Equal(["PlanId"], result.ValidationErrors!.Keys);
        Assert.Empty(f.Subscriptions.Grants);
    }

    [Theory]
    [InlineData(275, PlanGrantResultStatus.Success)]           // kết thúc đúng ngày thứ 365
    [InlineData(276, PlanGrantResultStatus.ExceedsMaxHorizon)]
    public async Task Grant_MayNotEndBeyondTheHorizon(int coveredDays, PlanGrantResultStatus expected)
    {
        var f = new Fixture();
        f.Subscriptions.Grants.Add(new PlanGrant
        {
            UserId = UserId,
            PlanId = TripPassId,
            PlanVersionId = SubscriptionBaseline.VersionId(PlanCode.TripPass),
            StartsAt = Now,
            EndsAt = Now.AddDays(coveredDays),
            Days = coveredDays,
            Source = PlanGrantSource.Admin
        });

        var result = await f.Service.GrantAsync(UserId, new(TripPassId, 90, "test"), AdminId);

        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == PlanGrantResultStatus.Success ? 2 : 1, f.Subscriptions.Grants.Count);
    }

    [Fact]
    public async Task GetByUser_UnknownUser_ReturnsNull_OtherwiseTheList()
    {
        var f = new Fixture();
        await f.Service.GrantAsync(UserId, new(TripPassId, 7, "test"), AdminId);
        Assert.Single((await f.Service.GetByUserAsync(UserId))!);
        f.UserStatus = null;
        Assert.Null(await f.Service.GetByUserAsync(UserId));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Lock = new FakeUserLock(this);
            Service = new PlanGrantService(new FakeGrantRepository(this), Subscriptions, Lock, Notifications, new FixedClock());
        }

        public UserStatus? UserStatus { get; set; } = Domain.Enums.UserStatus.Active;
        public MemorySubscriptions Subscriptions { get; } = new();
        public RecordingNotificationRepository Notifications { get; } = new();
        public FakeUserLock Lock { get; }
        public PlanGrantService Service { get; }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeUserLock(Fixture fixture) : ITripGenerationQuotaExecutor
    {
        public int Calls { get; private set; }

        public async Task<TripGenerationQuotaExecution<T>> ExecuteForUserAsync<T>(Guid userId,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return fixture.UserStatus is null ? new(false, default) : new(true, await operation(cancellationToken));
        }
    }

    private sealed class FakeGrantRepository(Fixture fixture) : IPlanGrantRepository
    {
        public Task<UserStatus?> GetUserStatusAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(fixture.UserStatus);

        public Task AddAsync(PlanGrant grant, CancellationToken ct = default)
        {
            fixture.Subscriptions.Grants.Add(grant);
            return Task.CompletedTask;
        }

        public Task<PlanGrantResponse?> GetAsync(Guid grantId, CancellationToken ct = default) =>
            Task.FromResult(fixture.Subscriptions.Grants.Where(g => g.Id == grantId).Select(ToResponse).SingleOrDefault());

        public Task<IReadOnlyList<PlanGrantResponse>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlanGrantResponse>>(
                fixture.Subscriptions.Grants.Where(g => g.UserId == userId).Select(ToResponse).ToArray());

        private static PlanGrantResponse ToResponse(PlanGrant g) => new(g.Id, g.UserId, new("TripPass", "Trip Pass"),
            g.Days, g.StartsAt, g.EndsAt, g.Source.ToString(), g.Reason, null, g.CreatedAt);
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
