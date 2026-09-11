using LocalMateAI.Application.DTOs.Geo;

namespace LocalMateAI.Application.Interfaces;

public interface IGeoService
{
    Task<NearestStationResult?> FindNearestStationAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);

    Task<NearestStationResult?> FindNearestStationForPlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);
}
