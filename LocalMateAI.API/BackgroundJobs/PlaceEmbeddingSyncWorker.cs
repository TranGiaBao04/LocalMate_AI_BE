using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.API.BackgroundJobs;

// Job nền: lúc khởi động và mỗi 10 phút, tính vector cho địa điểm mới hoặc vừa đổi nội dung.
// Thiếu khoá Embedding__ApiKey thì job không làm gì (tính năng tắt).
public sealed class PlaceEmbeddingSyncWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PlaceEmbeddingSyncWorker> logger) : BackgroundService
{
    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(10);

    private bool notConfiguredLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SyncInterval, timeProvider);

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
            var syncService = scope.ServiceProvider.GetRequiredService<IPlaceEmbeddingSyncService>();
            var result = await syncService.SyncAsync(stoppingToken);

            switch (result.Status)
            {
                case PlaceEmbeddingSyncStatus.NotConfigured when !notConfiguredLogged:
                    notConfiguredLogged = true;
                    logger.LogInformation("Embedding is not configured; semantic features are off");
                    break;
                case PlaceEmbeddingSyncStatus.ProviderUnavailable:
                    logger.LogWarning(
                        "Embedding provider unavailable; {EmbeddedCount} places embedded before the failure, will retry on next tick",
                        result.Embedded);
                    break;
                case PlaceEmbeddingSyncStatus.Completed when result.Embedded > 0 || result.Removed > 0:
                    logger.LogInformation(
                        "Place embeddings synced: {EmbeddedCount} embedded, {RemovedCount} removed",
                        result.Embedded,
                        result.Removed);
                    break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App đang tắt.
        }
        catch (Exception exception)
        {
            // Nuốt lỗi để job không chết (từ .NET 8, lỗi thoát khỏi BackgroundService sẽ dừng cả app).
            logger.LogError(exception, "Place embedding sync failed; will retry on next tick");
        }
    }
}
