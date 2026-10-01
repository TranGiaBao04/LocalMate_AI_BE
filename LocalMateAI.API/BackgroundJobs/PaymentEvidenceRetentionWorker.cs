using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Repositories;

namespace LocalMateAI.API.BackgroundJobs;

public sealed class PaymentEvidenceRetentionWorker(IServiceScopeFactory scopeFactory, TimeProvider clock,
    ILogger<PaymentEvidenceRetentionWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, clock);
        do { await RunOnceAsync(stoppingToken); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPaymentEvidenceRepository>();
            int purged;
            do { purged = await repository.PurgeExpiredRawPayloadsAsync(clock.GetUtcNow().UtcDateTime, cancellationToken); }
            while (purged == PaymentEvidenceRepository.PurgeBatchSize && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception e) { logger.LogError("Payment raw retention failed ({ErrorType}); will retry.", e.GetType().Name); }
    }
}
