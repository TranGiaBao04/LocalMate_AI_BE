namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record GrantPlanRequest(Guid PlanId, int Days, string? Reason);

public sealed record PlanGrantPlanResponse(string Code, string Name);

public sealed record PlanGrantActorResponse(Guid UserId, string FullName);

/// <summary><c>Reason</c> và <c>GrantedBy</c> là null với nguồn SignupTrial.</summary>
public sealed record PlanGrantResponse(
    Guid GrantId,
    Guid UserId,
    PlanGrantPlanResponse Plan,
    int Days,
    DateTime StartsAt,
    DateTime EndsAt,
    string Source,
    string? Reason,
    PlanGrantActorResponse? GrantedBy,
    DateTime CreatedAt);

public enum PlanGrantResultStatus
{
    Success,
    InvalidRequest,
    UserNotFound,
    UserLocked,
    ExceedsMaxHorizon
}

public sealed record PlanGrantResult(
    PlanGrantResultStatus Status,
    PlanGrantResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
