using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Services;

public sealed record AiUsageStart(AiUsageDecision Decision, AiUsageCall? Call = null);

public interface IAiUsageCoordinator
{
    Task<AiUsageStart> AdmitAsync(Guid userId, LlmCallKind kind, Guid? tripId = null,
        CancellationToken cancellationToken = default);
}
