using LocalMateAI.Application.DTOs.PublicStats;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Application.Services;

public sealed class PublicStatsService(
    IPublicStatsRepository repository,
    IMemoryCache cache,
    TimeProvider timeProvider) : IPublicStatsService
{
    public const string CacheKey = "public-stats";

    // API công khai, ai cũng gọi được ⇒ cache để DB chỉ đếm một lần mỗi CacheDuration.
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<PublicStatsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out PublicStatsResponse? cached) && cached is not null)
        {
            return cached;
        }

        // Lỗi DB ném ra ngoài nên không bị cache.
        var response = new PublicStatsResponse(
            await repository.CountFinalizedTripsAsync(cancellationToken),
            timeProvider.GetUtcNow().UtcDateTime);
        cache.Set(CacheKey, response, CacheDuration);
        return response;
    }
}
