using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Services;

namespace LocalMateAI.Application.Services;

public enum SubscriptionCheckoutClassification
{
    Purchase,
    Upgrade,
    SamePlan,
    CoveredByHigherPlan,
    TargetPlanAlreadyScheduled,
    InvalidConfiguration
}

public static class SubscriptionCheckoutPolicy
{
    public static SubscriptionCheckoutClassification Classify(
        EffectiveSubscription effective, SubscriptionPlan target, DateTime nowUtc,
        IReadOnlyList<SubscriptionPeriod> ownedPeriods)
    {
        if (effective.Period is null || effective.Plan.Code == PlanIdentity.Free)
            return SubscriptionCheckoutClassification.Purchase;
        if (target.Id == effective.Plan.Id)
            return SubscriptionCheckoutClassification.SamePlan;
        if (target.EntitlementPriority < effective.Plan.EntitlementPriority)
            return SubscriptionCheckoutClassification.CoveredByHigherPlan;
        if (target.EntitlementPriority == effective.Plan.EntitlementPriority)
            return SubscriptionCheckoutClassification.InvalidConfiguration;
        return ownedPeriods.Any(p => p.UserId == effective.Period.UserId && p.PlanId == target.Id
            && p.StartsAt > nowUtc && SubscriptionPeriodLifecycle.HasEffectiveDuration(p))
            ? SubscriptionCheckoutClassification.TargetPlanAlreadyScheduled
            : SubscriptionCheckoutClassification.Upgrade;
    }
}
