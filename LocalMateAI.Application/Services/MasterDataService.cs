using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Application.Services;

public sealed class MasterDataService(
    IMetroStationRepository metroStationRepository,
    IMemoryCache cache) : IMasterDataService
{
    private const string CacheKey = "master-data";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    public async Task<MasterDataResponse> GetMasterDataAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out MasterDataResponse? cached) && cached is not null)
        {
            return cached;
        }

        var stations = await metroStationRepository.GetAllAsync(cancellationToken);
        var categories = Enum.GetNames<PlaceCategory>();

        var result = new MasterDataResponse(stations, categories);

        cache.Set(CacheKey, result, CacheDuration);

        return result;
    }
}
