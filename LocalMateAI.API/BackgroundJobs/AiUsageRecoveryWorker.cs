using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;

namespace LocalMateAI.API.BackgroundJobs;

public sealed class AiUsageRecoveryWorker(IServiceScopeFactory scopeFactory, TimeProvider clock,
    ILogger<AiUsageRecoveryWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    public const int BatchSize = AiUsageAdmissionRepository.RecoveryBatchSize;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var timer = new PeriodicTimer(PollInterval, clock);
        try
        {
            do { await RunOnceAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        foreach (var state in new[] { AiUsageAdmissionState.Reserved, AiUsageAdmissionState.DispatchAuthorized })
        {
            if (cancellationToken.IsCancellationRequested) return;
            IReadOnlyList<AiUsageHandle> candidates;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                candidates = await scope.ServiceProvider.GetRequiredService<IAiUsageAdmissionRepository>()
                    .GetExpiredAsync(state, BatchSize, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "AI recovery scan failed for {State}; will retry next poll", state);
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (cancellationToken.IsCancellationRequested) return;
                try
                {
                    // Isolate failed transactions/contexts per item; never drain an unbounded backlog here.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var repository = scope.ServiceProvider.GetRequiredService<IAiUsageAdmissionRepository>();
                    if (state == AiUsageAdmissionState.Reserved)
                        await repository.ReleaseExpiredReservedAsync(candidate, cancellationToken);
                    else
                        await repository.AbandonExpiredDispatchAuthorizedAsync(candidate, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "AI recovery failed for attempt {AttemptId}; will retry next poll", candidate.AttemptId);
                }
            }
        }
    }
}
