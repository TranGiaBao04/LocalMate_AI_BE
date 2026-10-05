using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

internal sealed class TestAiUsageCoordinator : IAiUsageCoordinator, IAiUsageAdmissionRepository
{
    public AiUsageDecision Decision { get; set; } = new(AiUsageStatus.Allowed);
    public List<(Guid UserId, LlmCallKind Kind, Guid? TripId)> Checks { get; } = [];
    public List<(Guid UserId, LlmCallKind Kind, Guid? TripId, LlmCallOutcome Outcome, LlmJsonResponse? Response)> Records { get; } = [];
    public int Releases { get; private set; }
    public int Authorizations { get; private set; }

    public Task<AiUsageStart> AdmitAsync(Guid userId, LlmCallKind kind, Guid? tripId = null,
        CancellationToken cancellationToken = default)
    {
        Checks.Add((userId, kind, tripId));
        return Task.FromResult(new AiUsageStart(Decision, Decision.Status == AiUsageStatus.Allowed
            ? new AiUsageCall(new(Guid.NewGuid(), userId, kind, tripId, Guid.NewGuid(), 1), this,
                "model-x", NullLogger<AiUsageCoordinator>.Instance) : null));
    }

    public Task<AiUsageCompletionResult> CompleteAsync(AiUsageHandle handle, AiUsageCompletion completion,
        CancellationToken cancellationToken = default)
    {
        Records.Add((handle.UserId, handle.Kind, handle.TripIdSnapshot, completion.Outcome,
            completion.Outcome == LlmCallOutcome.ProviderFailed ? null
                : new LlmJsonResponse("", completion.InputTokens, completion.OutputTokens)));
        return Task.FromResult(new AiUsageCompletionResult(Guid.NewGuid(), true));
    }

    public Task<bool> AuthorizeDispatchAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration,
        CancellationToken cancellationToken = default)
    {
        Authorizations++;
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseReservedAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration,
        bool expiredOnly = false, CancellationToken cancellationToken = default)
    {
        Assert.False(cancellationToken.IsCancellationRequested);
        Releases++;
        return Task.FromResult(true);
    }

    Task<AiUsageAdmissionResult> IAiUsageAdmissionRepository.AdmitAsync(Guid attemptId, Guid userId, LlmCallKind kind,
        Guid? tripId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> CountDailyAsync(Guid userId, DateOnly date, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AiUsageHandle>> GetExpiredAsync(AiUsageAdmissionState state, int batchSize, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> ReleaseExpiredReservedAsync(AiUsageHandle handle, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> AbandonExpiredDispatchAuthorizedAsync(AiUsageHandle handle, CancellationToken ct = default) => throw new NotSupportedException();
}
