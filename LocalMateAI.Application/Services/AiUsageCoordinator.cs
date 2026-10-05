using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class AiUsageCoordinator(IAiUsageAdmissionRepository repository, ILlmClient client,
    ISystemSettingProvider settings, ILogger<AiUsageCoordinator> logger) : IAiUsageCoordinator
{
    public async Task<AiUsageStart> AdmitAsync(Guid userId, LlmCallKind kind, Guid? tripId = null,
        CancellationToken cancellationToken = default)
    {
        if (!client.IsConfigured || await settings.GetIntAsync(SystemSettingKeys.AiEnabled, cancellationToken) == 0)
            return new(new(AiUsageStatus.Disabled));

        var result = await repository.AdmitAsync(Guid.NewGuid(), userId, kind, tripId, cancellationToken);
        if (result.Decision.Status != AiUsageStatus.Allowed) return new(result.Decision);
        var a = result.Admission ?? throw new InvalidOperationException("Allowed admission requires a durable attempt.");
        return new(result.Decision, new AiUsageCall(new(a.Id, a.UserId, a.Kind, a.TripIdSnapshot,
            a.FencingToken, a.FencingGeneration), repository, client.Model, logger));
    }
}

// A local lifetime scope, not a quota lock. PostgreSQL remains the sole admission authority.
public sealed class AiUsageCall(AiUsageHandle handle, IAiUsageAdmissionRepository repository,
    string model, ILogger<AiUsageCoordinator> logger) : IAsyncDisposable
{
    private bool authorized;
    private bool released;

    public async Task AuthorizeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (authorized || released || !await repository.AuthorizeDispatchAsync(handle.UserId, handle.AttemptId,
                handle.FencingToken, handle.FencingGeneration, ct))
            throw new InvalidOperationException("AI dispatch authorization was lost or expired.");
        authorized = true;
    }

    public Task<AiUsageCompletionResult> CompleteAsync(LlmCallOutcome outcome, LlmJsonResponse? response,
        int durationMilliseconds)
    {
        if (!authorized) throw new InvalidOperationException("AI completion requires authorized dispatch.");
        return repository.CompleteAsync(handle, new(outcome, model, response?.InputTokens ?? 0,
            response?.OutputTokens ?? 0, durationMilliseconds), CancellationToken.None);
    }

    public async Task ReleaseAsync()
    {
        if (authorized || released || !await repository.ReleaseReservedAsync(handle.UserId, handle.AttemptId,
                handle.FencingToken, handle.FencingGeneration, cancellationToken: CancellationToken.None))
            throw new InvalidOperationException("AI reservation could not be released.");
        released = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (authorized || released) return;
        try { await ReleaseAsync(); }
        catch (Exception exception)
        {
            // Preserve the original failure; durable state remains available to S5D recovery.
            logger.LogWarning(exception, "Could not release pre-dispatch AI attempt {AttemptId}", handle.AttemptId);
        }
    }
}
