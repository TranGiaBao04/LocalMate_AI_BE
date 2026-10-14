using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class PlanGrantRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Low = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();
    private static readonly Guid High = Guid.NewGuid();
    private static readonly Dictionary<Guid, int> Priorities = new() { [Low] = 100, [Target] = 250, [High] = 300 };

    [Fact]
    public void NothingCovered_StartsNow() =>
        Assert.Equal(Now, PlanGrantRules.StartsAt(Now, 250, [], [], Priorities));

    [Theory]
    [InlineData("target", 6)] // cùng gói đã mua: nối sau
    [InlineData("high", 6)]   // gói cao hơn đã mua: nối sau
    [InlineData("low", 0)]    // gói thấp hơn không che gói tặng: bắt đầu ngay
    public void PurchasedPeriod_DelaysOnlyWhenSameOrHigherPriority(string owned, int expectedDay)
    {
        var planId = owned switch { "target" => Target, "high" => High, _ => Low };
        var start = PlanGrantRules.StartsAt(Now, 250, [Period(planId, Now.AddDays(-1), Now.AddDays(6))], [], Priorities);
        Assert.Equal(Now.AddDays(expectedDay), start);
    }

    [Fact]
    public void LatestCoverageWins_AcrossFuturePeriodsAndGrants()
    {
        var periods = new[] { Period(Target, Now.AddDays(-1), Now.AddDays(6)), Period(Target, Now.AddDays(6), Now.AddDays(13)) };
        var grants = new[] { Grant(Target, Now.AddDays(13), Now.AddDays(20)), Grant(Low, Now, Now.AddDays(40)) };
        Assert.Equal(Now.AddDays(20), PlanGrantRules.StartsAt(Now, 250, periods, grants, Priorities));
    }

    [Fact]
    public void ExpiredTerminatedAndUnknownPlanCoverage_IsIgnored()
    {
        var terminated = Period(High, Now.AddDays(-5), Now.AddDays(20));
        terminated.Terminate(Now.AddDays(-1), Guid.NewGuid());
        var periods = new[] { terminated, Period(Target, Now.AddDays(-10), Now), Period(Guid.NewGuid(), Now, Now.AddDays(9)) };
        var grants = new[] { Grant(Target, Now.AddDays(-7), Now) };
        Assert.Equal(Now, PlanGrantRules.StartsAt(Now, 250, periods, grants, Priorities));
    }

    [Fact]
    public void ChainEnd_FollowsBackToBackGrants_AndStopsAtTheFirstGap()
    {
        var others = new[]
        {
            (Now.AddDays(10), Now.AddDays(13)), // nối sau gói thứ hai
            (Now.AddDays(7), Now.AddDays(10)),  // nối sau gói đang chạy
            (Now.AddDays(15), Now.AddDays(20)), // cách một khoảng nên không nối
            (Now.AddDays(-7), Now)              // đã hết từ trước
        };

        Assert.Equal(Now.AddDays(13), PlanGrantRules.ChainEnd(Now.AddDays(7), others));
        Assert.Equal(Now.AddDays(7), PlanGrantRules.ChainEnd(Now.AddDays(7), []));
    }

    private static SubscriptionPeriod Period(Guid planId, DateTime start, DateTime end) =>
        new() { UserId = Guid.NewGuid(), PlanId = planId, StartsAt = start, EndsAt = end };

    private static PlanGrant Grant(Guid planId, DateTime start, DateTime end) =>
        new() { UserId = Guid.NewGuid(), PlanId = planId, StartsAt = start, EndsAt = end, Days = 1 };
}
