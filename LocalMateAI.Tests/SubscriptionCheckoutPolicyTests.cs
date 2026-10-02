using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class SubscriptionCheckoutPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false, 100, 200, SubscriptionCheckoutClassification.Purchase)]
    [InlineData(true, true, 100, 100, SubscriptionCheckoutClassification.SamePlan)]
    [InlineData(true, false, 200, 100, SubscriptionCheckoutClassification.CoveredByHigherPlan)]
    [InlineData(true, false, 100, 200, SubscriptionCheckoutClassification.Upgrade)]
    [InlineData(true, false, 100, 100, SubscriptionCheckoutClassification.InvalidConfiguration)]
    public void DirectionUsesIdentityAndPriority_NotPriceOrName(bool paid, bool same,
        int currentPriority, int targetPriority, SubscriptionCheckoutClassification expected)
    {
        var current = new SubscriptionPlan { Code = "CUSTOM_CURRENT", EntitlementPriority = currentPriority };
        var target = new SubscriptionPlan { Id = same ? current.Id : Guid.NewGuid(),
            Code = "CHEAPER_CUSTOM", EntitlementPriority = targetPriority };
        var version = new SubscriptionPlanVersion { PlanId = current.Id, Price = 999999, DurationDays = 30 };
        var period = paid ? new SubscriptionPeriod { UserId = Guid.NewGuid(), PlanId = current.Id,
            PlanVersionId = version.Id, StartsAt = Now.AddDays(-1), EndsAt = Now.AddDays(29) } : null;
        Assert.Equal(expected, SubscriptionCheckoutPolicy.Classify(
            new(current, version, period, period?.EndsAt), target, Now, []));
    }

    [Theory]
    [InlineData(false, false, SubscriptionCheckoutClassification.TargetPlanAlreadyScheduled)]
    [InlineData(true, false, SubscriptionCheckoutClassification.Upgrade)]
    [InlineData(false, true, SubscriptionCheckoutClassification.Upgrade)]
    public void FutureTargetBlocksOnlyOwnedUnterminatedPeriod(bool terminated, bool differentOwner,
        SubscriptionCheckoutClassification expected)
    {
        var user = Guid.NewGuid();
        var current = SubscriptionBaseline.Plans().Single(p => p.Code == "TRIP_PASS");
        var target = SubscriptionBaseline.Plans().Single(p => p.Code == "MEMBERSHIP");
        var version = SubscriptionBaseline.Versions().Single(v => v.PlanId == current.Id);
        var active = new SubscriptionPeriod { UserId = user, PlanId = current.Id, StartsAt = Now.AddDays(-1),
            EndsAt = Now.AddDays(6), PlanVersionId = version.Id };
        var future = new SubscriptionPeriod { UserId = differentOwner ? Guid.NewGuid() : user,
            PlanId = target.Id, StartsAt = Now.AddDays(6), EndsAt = Now.AddDays(36) };
        if (terminated) future.Terminate(Now, Guid.NewGuid());
        Assert.Equal(expected, SubscriptionCheckoutPolicy.Classify(new(current, version, active, active.EndsAt),
            target, Now, [future]));
    }

    [Fact]
    public void ExplicitFreeFallbackIsPurchase()
    {
        var free = SubscriptionBaseline.Plans().Single(p => p.Code == "FREE");
        var version = SubscriptionBaseline.Versions().Single(v => v.PlanId == free.Id);
        var target = SubscriptionBaseline.Plans().Single(p => p.Code == "TRIP_PASS");
        Assert.Equal(SubscriptionCheckoutClassification.Purchase,
            SubscriptionCheckoutPolicy.Classify(new(free, version, new(), null), target, Now, []));
    }
}
