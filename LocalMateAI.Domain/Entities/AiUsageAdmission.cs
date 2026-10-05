using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

// Logical admission timestamps deliberately do not use BaseEntity's SaveChanges audit clock.
public sealed class AiUsageAdmission
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? TripId { get; set; }
    public Guid? TripIdSnapshot { get; init; }
    public LlmCallKind Kind { get; init; }
    public DateOnly VietnamUsageDate { get; init; }
    public AiUsageAdmissionState State { get; set; }
    public DateTime AdmittedAt { get; init; }
    public DateTime ReservedUntil { get; init; }
    public DateTime? DispatchAuthorizedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RecoveryAfter { get; set; }
    public LlmCallOutcome? Outcome { get; set; }
    public Guid? LlmCallLogId { get; set; }
    public Guid ResolvedPlanVersionId { get; init; }
    public int AdmittedDailyLimit { get; init; }
    public int? AdmittedExplainLimit { get; init; }
    public Guid FencingToken { get; set; }
    public long FencingGeneration { get; set; }
}
