using System.Text.Json.Serialization;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Payments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EntitlementRepairRequest(string? Reason);

public sealed record EntitlementEvidence(string GrantStatus, Guid? SubscriptionPeriodId = null,
    DateTime? StartsAt = null, DateTime? EndsAt = null);
public sealed record EntitlementRepairEligibility(bool Eligible, string Code, string Message,
    DateTime? ProposedStartsAt, DateTime? ProposedEndsAt, string? ReconstructionMode, DateTime AssessedAt);
public sealed record EntitlementRepairAssessment(EntitlementEvidence Entitlement,
    EntitlementRepairEligibility Eligibility, bool IsConflict = false);
public sealed record EntitlementRepairResponse(EntitlementRepairOutcome Result, Guid OrderId,
    Guid? SubscriptionPeriodId, DateTime? StartsAt, DateTime? EndsAt, Guid AuditId,
    string DecisionCode, string? ReconstructionMode);
public sealed record EntitlementRepairHistoryResponse(Guid Id, Guid? SubscriptionPeriodId, Guid ActorUserId,
    string Reason, string Outcome, string DecisionCode, string? ReconstructionMode, DateTime OccurredAt);
public sealed record EntitlementRepairServiceResult(bool InvalidRequest, EntitlementRepairResponse? Response = null);

// Safe server-side projections shared by the detail snapshot and the repair executor.
public sealed record RepairOrderEvidence(Guid Id, Guid UserId, Guid? PlanId, Guid? PlanVersionId,
    PlanVersionBinding? Binding, PaymentOrderStatus Status, decimal Amount, DateTime? PaidAt)
{
    public PaymentProductKind ProductKind { get; init; } = PaymentProductKind.SubscriptionPlan;
}
public sealed record RepairVersionEvidence(Guid Id, Guid PlanId, string PlanCode, decimal Price, int? DurationDays);
public sealed record RepairPurchaseEvidence(RepairOrderEvidence Order, RepairVersionEvidence? Version);
public sealed record EntitlementRepairEvidence(bool UserExists, RepairPurchaseEvidence Target,
    IReadOnlyList<RepairPurchaseEvidence> Purchases, IReadOnlyList<SubscriptionPeriod> Periods);

public static class EntitlementRepairReason
{
    public static bool TryNormalize(string? reason, out string normalized)
    {
        normalized = reason?.Trim() ?? string.Empty;
        return normalized.Length is >= 1 and <= 500 && reason is not null && !reason.Any(char.IsControl);
    }
}
