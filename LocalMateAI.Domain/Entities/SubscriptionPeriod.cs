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
    public DateTime? TerminatedAt { get; set; }
    public Guid? TerminatedByOrderId { get; set; }

    public void Terminate(DateTime atUtc, Guid orderId)
    {
        if (atUtc.Kind != DateTimeKind.Utc || orderId == Guid.Empty
            || TerminatedAt is not null || TerminatedByOrderId is not null)
            throw new InvalidOperationException("Period termination is a one-way UTC transition.");
        TerminatedAt = atUtc;
        TerminatedByOrderId = orderId;
    }
}
