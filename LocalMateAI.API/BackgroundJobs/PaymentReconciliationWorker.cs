using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Options;

namespace LocalMateAI.API.BackgroundJobs;

public sealed class PaymentReconciliationWorker(IServiceScopeFactory scopes, IOptions<PaymentReconciliationOptions> options,
    TimeProvider clock, ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), clock);
        try
        {
            do { await RunOnceAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var attempted = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            var leases = scope.ServiceProvider.GetRequiredService<IPaymentReconciliationLeaseProvider>();
            await using var lease = await leases.TryAcquireAsync(cancellationToken);
            if (lease is null)
            {
                logger.LogDebug("Payment reconciliation batch skipped: another instance owns the lease.");
                return 0;
            }
            var orders = scope.ServiceProvider.GetRequiredService<IPaymentOrderRepository>();
            var candidates = await orders.GetExpiredPendingIdsAsync(clock.GetUtcNow().UtcDateTime, options.Value.BatchSize, cancellationToken);
            foreach (var id in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A failed candidate cannot leave tracked state in the next settlement scope.
                await using var itemScope = scopes.CreateAsyncScope();
                var core = itemScope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>();
                attempted++;
                try
                {
                    var result = await core.ReconcileAsync(id, new(PaymentStatusChangeSource.BackgroundReconcile), cancellationToken);
                    logger.LogInformation("Payment reconciliation worker {OrderId}: {Result}, provider {ProviderStatus}.",
                        id, result.Status, result.ProviderStatus);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception e) { logger.LogWarning("Payment reconciliation candidate {OrderId} failed ({ErrorType}); retry later.", id, e.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) { logger.LogWarning("Payment reconciliation batch failed ({ErrorType}); retry later.", e.GetType().Name); }
        return attempted;
    }
}
