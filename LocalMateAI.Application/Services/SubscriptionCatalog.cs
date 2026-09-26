using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed record SubscriptionPlanDefinition(
    PlanCode Code,
    decimal Price,
    int? DurationDays,
    int? GenerateLimit,
    int? SavedTripLimit);

public static class SubscriptionCatalog
{
    private static readonly IReadOnlyList<SubscriptionPlanDefinition> Plans = Array.AsReadOnly<SubscriptionPlanDefinition>(
    [
        new(PlanCode.Free, 0, null, 1, 1),
        new(PlanCode.TripPass, 49_000, 7, null, 3),
        new(PlanCode.Membership, 59_000, 30, null, null)
    ]);

    public static IReadOnlyList<SubscriptionPlanDefinition> All => Plans;

    public static SubscriptionPlanDefinition Get(PlanCode code) =>
        Plans.Single(plan => plan.Code == code);

    public static UserSubscription? ResolveEffectivePaid(
        IEnumerable<UserSubscription> subscriptions,
        DateTime nowUtc) =>
        subscriptions
            .Where(subscription => subscription.EndsAt > nowUtc)
            .Where(subscription => subscription.PlanCode is PlanCode.Membership or PlanCode.TripPass)
            .OrderByDescending(subscription => subscription.PlanCode == PlanCode.Membership)
            .ThenByDescending(subscription => subscription.EndsAt)
            .FirstOrDefault();
}
