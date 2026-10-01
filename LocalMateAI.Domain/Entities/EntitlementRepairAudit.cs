namespace LocalMateAI.Domain.Entities;

public enum EntitlementRepairOutcome { Repaired, AlreadyGranted, NotEligible, Conflict }

public sealed class EntitlementRepairAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PaymentOrderId { get; init; }
    public Guid? SubscriptionPeriodId { get; init; }
    public Guid ActorUserId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public EntitlementRepairOutcome Outcome { get; init; }
    public string DecisionCode { get; init; } = string.Empty;
    public string? ReconstructionMode { get; init; }
    public DateTime OccurredAt { get; init; }
}
