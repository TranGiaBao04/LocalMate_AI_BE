using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class GeoService(
    IMetroStationRepository stationRepository,
    IPlaceRepository placeRepository) : IGeoService
{
    public Task<NearestStationResult?> FindNearestStationAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
        => stationRepository.FindNearestAsync(latitude, longitude, cancellationToken);

    public async Task<NearestStationResult?> FindNearestStationForPlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var location = await placeRepository.GetLocationAsync(placeId, cancellationToken);

        return location is null
            ? null
            : await FindNearestStationAsync(location.Y, location.X, cancellationToken);
    }
}
