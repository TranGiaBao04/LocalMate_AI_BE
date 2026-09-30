namespace LocalMateAI.Domain.Entities;

public sealed class SubscriptionPlanVersionFeature
{
    public Guid PlanVersionId { get; init; }
    public Guid FeatureId { get; init; }
    public int SortOrder { get; init; }
}
