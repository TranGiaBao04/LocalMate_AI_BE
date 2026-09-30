using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;

namespace LocalMateAI.API.BackgroundJobs;

// Job nền: mỗi 30 giây gửi mail đến hạn trong outbox, mỗi giờ dọn mail cũ.
// Chỉ chạy khi process API còn sống — hosting "ngủ" sẽ làm mail chậm (không mất).
public sealed class EmailOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<EmailOutboxDispatcher> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private DateTimeOffset nextCleanupAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IEmailOutboxProcessor>();

            // Nhận đủ 1 lô nghĩa là có thể còn mail chờ → gửi tiếp luôn, không đợi 30 giây.
            int claimed;
            do
            {
                claimed = await processor.ProcessDueAsync(stoppingToken);
            }
            while (claimed == EmailOutboxProcessor.BatchSize && !stoppingToken.IsCancellationRequested);

            if (timeProvider.GetUtcNow() >= nextCleanupAt)
            {
                var deleted = await processor.DeleteExpiredAsync(stoppingToken);
                nextCleanupAt = timeProvider.GetUtcNow().Add(CleanupInterval);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {DeletedCount} expired email outbox messages", deleted);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App đang tắt.
        }
        catch (Exception exception)
        {
            // Nuốt lỗi để job không chết (từ .NET 8, lỗi thoát khỏi BackgroundService sẽ dừng cả app).
            logger.LogError(exception, "Email outbox dispatch failed; will retry on next tick");
        }
    }
}
