using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
namespace LocalMateAI.Tests;

public sealed class EffectiveSubscriptionTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData(-1, "LOW")]
    [InlineData(0, "HIGH")]
    [InlineData(2, "HIGH")]
    [InlineData(3, "LOW")]
    [InlineData(4, "LOW")]
    [InlineData(5, "FREE")]
    public async Task HalfOpenPriority_MaskingContinuesClockAndUsesOneWholeVersion(int day, string expected)
    {
        var repo = new MemoryRepository();
        var low = Add(repo, "LOW", 250, Now.AddDays(-1), Now.AddDays(5), 9, 1);
        var high = Add(repo, "HIGH", 300, Now, Now.AddDays(3), 0, 99);
        high.Plan.IsActive = false;
        var result = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now.AddDays(day));
        Assert.Equal(expected, result.Plan.Code);
        if (expected == "HIGH")
        {
            Assert.Equal(0, result.Version.GenerateLimit); // not max(9,0), not union
            Assert.Equal(99, result.Version.SavedTripLimit);
            Assert.Equal(high.Period.Id, result.Period!.Id);
        }
        Assert.Equal(Now.AddDays(5), low.Period.EndsAt);
    }
    [Fact]
    public async Task CurrentVersionChange_DoesNotChangePaidVersion_ButFreeIsCurrent()
    {
        var repo = new MemoryRepository();
        var paid = Add(repo, "CUSTOM", 250, Now, Now.AddDays(1), 1, 3);
        var v2 = new SubscriptionPlanVersion
        {
            PlanId = paid.Plan.Id,
            VersionNumber = 2,
            Price = 69000,
            DurationDays = 30,
            GenerateLimit = 0,
            SavedTripLimit = 1,
            Origin = PlanVersionOrigin.Published,
            PublishedAt = Now
        };
        await repo.PublishVersionAsync(v2);
        var current = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now);
        Assert.Equal(paid.Version.Id, current.Version.Id);
        var freePlan = repo.Plans.Single(p => p.Code == "FREE");
        var newFree = new SubscriptionPlanVersion { PlanId = freePlan.Id, VersionNumber = 2, GenerateLimit = 2, SavedTripLimit = 2 };
        await repo.PublishVersionAsync(newFree);
        var expired = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, paid.Period.EndsAt);
        Assert.Equal(newFree.Id, expired.Version.Id);
        Assert.Null(expired.Period);
    }
    [Fact]
    public async Task QueuedRenewal_KeepsCurrentBoundaryAndContinuousPaidThrough()
    {
        var repo = new MemoryRepository();
        var first = Add(repo, "CUSTOM", 250, Now, Now.AddDays(7), 1, 3);
        var v2 = new SubscriptionPlanVersion { PlanId = first.Plan.Id, VersionNumber = 2, GenerateLimit = 2 };
        await repo.PublishVersionAsync(v2);
        var next = new SubscriptionPeriod
        {
            UserId = UserId,
            PlanId = first.Plan.Id,
            PlanVersionId = v2.Id,
            StartsAt = first.Period.EndsAt,
            EndsAt = Now.AddDays(17),
            SourcePaymentOrderId = Guid.NewGuid()
        };
        repo.Periods.Add(next);
        var before = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now.AddDays(7).AddTicks(-1));
        var after = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now.AddDays(7));
        Assert.Equal(first.Version.Id, before.Version.Id);
        Assert.Equal(first.Period.EndsAt, before.EffectiveUntil);
        Assert.Equal(next.EndsAt, before.PaidThrough);
        Assert.Equal(v2.Id, after.Version.Id);
        Assert.Equal(next.Id, after.Period!.Id);
    }
    [Fact]
    public async Task FuturePeriod_DoesNotActivateEarly()
    {
        var repo = new MemoryRepository();
        Add(repo, "CUSTOM", 300, Now.AddDays(1), Now.AddDays(8), 5, 5);
        var current = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now);
        Assert.Equal("FREE", current.Plan.Code);
        Assert.Null(current.Period);
    }
    [Fact]
    public async Task ExpiredMaskedLower_DoesNotReturnAfterHigherEnds()
    {
        var repo = new MemoryRepository();
        Add(repo, "LOW", 250, Now, Now.AddDays(1), 9, 1);
        Add(repo, "HIGH", 300, Now, Now.AddDays(2), 1, 9);
        Assert.Equal("FREE", (await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now.AddDays(2))).Plan.Code);
    }
    [Theory]
    [InlineData(-1, "FREE", false)]
    [InlineData(0, "GIFT", true)]
    [InlineData(6, "GIFT", true)]
    [InlineData(7, "FREE", false)]
    public async Task Grant_IsEffectiveOnHalfOpenRange_WithoutPeriod(int day, string expected, bool trial)
    {
        var repo = new MemoryRepository();
        var grant = AddGrant(repo, AddPlan(repo, "GIFT", 250, null, 3), Now, Now.AddDays(7));
        var result = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now.AddDays(day));
        Assert.Equal(expected, result.Plan.Code);
        Assert.Equal(trial, result.IsTrial);
        Assert.Null(result.Period);
        Assert.Null(result.PaidThrough);
        Assert.Equal(trial ? grant.EndsAt : null, result.EffectiveUntil);
        Assert.Null(result.GenerateResetAt);
    }
    [Fact]
    public async Task PurchasedPeriod_WinsOverGrantOfSamePlan_ThenGrantTakesOver()
    {
        var repo = new MemoryRepository();
        var paid = Add(repo, "SAME", 250, Now, Now.AddDays(3), null, 3);
        AddGrant(repo, (paid.Plan, paid.Version), Now, Now.AddDays(7));
        var during = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now.AddDays(1));
        Assert.False(during.IsTrial);
        Assert.Equal(paid.Period.Id, during.Period!.Id);
        Assert.Equal(paid.Period.EndsAt, during.GenerateResetAt);
        var after = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now.AddDays(3));
        Assert.True(after.IsTrial);
        Assert.Equal(Now.AddDays(7), after.EffectiveUntil);
    }
    [Fact]
    public async Task HigherPriorityWins_WhicheverSideIsTheGrant()
    {
        var grantedHigh = new MemoryRepository();
        Add(grantedHigh, "LOW", 250, Now, Now.AddDays(7), 9, 1);
        AddGrant(grantedHigh, AddPlan(grantedHigh, "HIGH", 300, null, 9), Now, Now.AddDays(2));
        var trial = await EffectiveSubscriptionResolver.ResolveAsync(grantedHigh, UserId, Now);
        Assert.Equal("HIGH", trial.Plan.Code);
        Assert.True(trial.IsTrial);
        var back = await EffectiveSubscriptionResolver.ResolveAsync(grantedHigh, UserId, Now.AddDays(2));
        Assert.Equal("LOW", back.Plan.Code);
        Assert.False(back.IsTrial);

        var grantedLow = new MemoryRepository();
        Add(grantedLow, "HIGH", 300, Now, Now.AddDays(2), null, 9);
        AddGrant(grantedLow, AddPlan(grantedLow, "LOW", 250, null, 1), Now, Now.AddDays(7));
        var paid = await EffectiveSubscriptionResolver.ResolveAsync(grantedLow, UserId, Now);
        Assert.Equal("HIGH", paid.Plan.Code);
        Assert.False(paid.IsTrial);
    }
    [Fact]
    public async Task ChainedGrantsOfOnePlan_ReportTheEndOfTheChain()
    {
        var repo = new MemoryRepository();
        var gift = AddPlan(repo, "GIFT", 250, null, 3);
        AddGrant(repo, gift, Now, Now.AddDays(7));
        AddGrant(repo, gift, Now.AddDays(7), Now.AddDays(10));
        AddGrant(repo, gift, Now.AddDays(12), Now.AddDays(15)); // có khoảng hở nên không nối
        var result = await EffectiveSubscriptionResolver.ResolveAsync(repo, UserId, Now);
        Assert.Equal(Now.AddDays(10), result.EffectiveUntil);
    }
    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) AddPlan(
        MemoryRepository repo, string code, int priority, int? generate, int? saved)
    {
        var plan = new SubscriptionPlan { Code = code, Name = code, EntitlementPriority = priority };
        var version = new SubscriptionPlanVersion
        {
            PlanId = plan.Id,
            VersionNumber = 1,
            Price = 19000,
            DurationDays = 7,
            GenerateLimit = generate,
            SavedTripLimit = saved
        };
        plan.CurrentVersionId = version.Id;
        repo.Plans.Add(plan); repo.Versions.Add(version);
        return (plan, version);
    }
    private static PlanGrant AddGrant(MemoryRepository repo,
        (SubscriptionPlan Plan, SubscriptionPlanVersion Version) target, DateTime start, DateTime end)
    {
        var grant = new PlanGrant
        {
            UserId = UserId,
            PlanId = target.Plan.Id,
            PlanVersionId = target.Version.Id,
            StartsAt = start,
            EndsAt = end,
            Days = (end - start).Days,
            Source = PlanGrantSource.Admin,
            Reason = "test"
        };
        repo.Grants.Add(grant);
        return grant;
    }
    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version, SubscriptionPeriod Period) Add(
        MemoryRepository repo, string code, int priority, DateTime start, DateTime end, int? generate, int? saved)
    {
        var plan = new SubscriptionPlan { Code = code, Name = code, EntitlementPriority = priority };
        var version = new SubscriptionPlanVersion
        {
            PlanId = plan.Id,
            VersionNumber = 1,
            Price = 19000,
            DurationDays = 7,
            GenerateLimit = generate,
            SavedTripLimit = saved
        };
        plan.CurrentVersionId = version.Id;
        var period = new SubscriptionPeriod
        {
            UserId = UserId,
            PlanId = plan.Id,
            PlanVersionId = version.Id,
            StartsAt = start,
            EndsAt = end,
            SourcePaymentOrderId = Guid.NewGuid()
        };
        repo.Plans.Add(plan); repo.Versions.Add(version); repo.Periods.Add(period);
        return (plan, version, period);
    }
    private sealed class MemoryRepository : TestSubscriptionRepository
    {
        public override Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UserSubscription>>([]);
        public override Task<UserSubscription?> GetByUserAndPlanAsync(Guid userId, PlanCode code, CancellationToken ct = default) =>
            Task.FromResult<UserSubscription?>(null);
        public override Task AddAsync(UserSubscription s, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
