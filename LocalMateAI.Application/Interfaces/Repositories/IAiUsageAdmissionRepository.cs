using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public sealed record AiUsageAdmissionResult(AiUsageDecision Decision, AiUsageAdmission? Admission = null);

public sealed record AiUsageHandle(Guid AttemptId, Guid UserId, LlmCallKind Kind, Guid? TripIdSnapshot,
    Guid FencingToken, long FencingGeneration);

public sealed record AiUsageCompletion(LlmCallOutcome Outcome, string Model, int InputTokens,
    int OutputTokens, int DurationMilliseconds);

/// <param name="CountsTowardDailyLimit">false khi lần lỗi do nhà cung cấp còn nằm trong số lần được miễn của ngày.</param>
/// <param name="RetryAfterSeconds">Số giây phải chờ trước lần gọi AI kế tiếp; 0 khi lần gọi này không bắt đầu thời gian chờ.</param>
public sealed record AiUsageCompletionResult(Guid LlmCallLogId, bool CompletedNow,
    bool CountsTowardDailyLimit = true, int RetryAfterSeconds = 0);

public interface IAiUsageAdmissionRepository : IAiUsageAccountingRepository
{
    Task<AiUsageAdmissionResult> AdmitAsync(Guid attemptId, Guid userId, LlmCallKind kind,
        Guid? tripId = null, CancellationToken cancellationToken = default);

    Task<bool> ReleaseReservedAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration,
        bool expiredOnly = false, CancellationToken cancellationToken = default);

    Task<bool> AuthorizeDispatchAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration,
        CancellationToken cancellationToken = default);

    Task<AiUsageCompletionResult> CompleteAsync(AiUsageHandle handle, AiUsageCompletion completion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiUsageHandle>> GetExpiredAsync(AiUsageAdmissionState state, int batchSize,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseExpiredReservedAsync(AiUsageHandle handle, CancellationToken cancellationToken = default);

    Task<bool> AbandonExpiredDispatchAuthorizedAsync(AiUsageHandle handle, CancellationToken cancellationToken = default);
}
