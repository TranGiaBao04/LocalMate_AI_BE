using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

public static class SubscriptionCheckoutContext
{
    public static async Task<(SubscriptionPlan Plan, SubscriptionPlanVersion Version)?> GetTargetAsync(
        ISubscriptionRepository repository, string? planCode, CancellationToken cancellationToken)
    {
        var code = PlanIdentity.Canonical(planCode);
        if (string.IsNullOrWhiteSpace(code)) return null;
        var plan = await repository.GetPlanByCodeAsync(code, cancellationToken);
        if (plan is null || !plan.IsActive || plan.Code == PlanIdentity.Free
            || plan.CurrentVersionId is not { } id) return null;
        var version = await repository.GetVersionAsync(id, cancellationToken);
        return version is null || version.PlanId != plan.Id || version.DurationDays is not > 0
            || version.Price <= 0 || decimal.Truncate(version.Price) != version.Price
            ? null : (plan, version);
    }

    public static async Task<SubscriptionCheckoutClassification> ClassifyAsync(
        ISubscriptionRepository repository, Guid userId, SubscriptionPlan target, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var effective = await EffectiveSubscriptionResolver.ResolveAsync(repository, userId, nowUtc, cancellationToken);
        return SubscriptionCheckoutPolicy.Classify(effective, target, nowUtc,
            await repository.GetPeriodsAsync(userId, cancellationToken));
    }

    public static PaymentIntentResultStatus? ClassificationError(SubscriptionCheckoutClassification classification) =>
        classification switch
        {
            SubscriptionCheckoutClassification.SamePlan => PaymentIntentResultStatus.PlanAlreadyActive,
            SubscriptionCheckoutClassification.CoveredByHigherPlan => PaymentIntentResultStatus.CoveredByHigherPlan,
            SubscriptionCheckoutClassification.TargetPlanAlreadyScheduled => PaymentIntentResultStatus.TargetPlanAlreadyScheduled,
            SubscriptionCheckoutClassification.InvalidConfiguration => PaymentIntentResultStatus.InvalidPlanCode,
            _ => null
        };
}
