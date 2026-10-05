using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public sealed record AiUsageAdmissionResult(AiUsageDecision Decision, AiUsageAdmission? Admission = null);

public interface IAiUsageAdmissionRepository
{
    Task<AiUsageAdmissionResult> AdmitAsync(Guid attemptId, Guid userId, LlmCallKind kind,
        Guid? tripId = null, CancellationToken cancellationToken = default);

    Task<bool> ReleaseReservedAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration,
        bool expiredOnly = false, CancellationToken cancellationToken = default);

    Task<bool> AuthorizeDispatchAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration,
        CancellationToken cancellationToken = default);
}
