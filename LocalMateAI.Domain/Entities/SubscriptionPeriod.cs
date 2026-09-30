using LocalMateAI.Domain.Common;
namespace LocalMateAI.Domain.Entities;

public sealed class SubscriptionPeriod : BaseEntity
{
    public Guid UserId { get; init; }
    public Guid PlanId { get; init; }
    public Guid PlanVersionId { get; init; }
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public Guid? SourcePaymentOrderId { get; init; }
    public Guid? LegacyUserSubscriptionId { get; init; }
}
