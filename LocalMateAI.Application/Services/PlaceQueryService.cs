using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class PlaceQueryService(
    IPlaceRepository placeRepository,
    IGeoService geoService) : IPlaceQueryService
{
    private const double NearbyRadiusMeters = 800;

    public async Task<NearbyPlacesResponse> GetPlacesNearUserAsync(
        double latitude,
        double longitude,
        PlaceCategory? category,
        CancellationToken cancellationToken = default)
    {
        var station = await geoService.FindNearestStationAsync(latitude, longitude, cancellationToken)
            ?? throw new InvalidOperationException("No metro stations found.");

        var places = await placeRepository.GetActiveWithinRadiusAsync(
            station.StationLatitude,
            station.StationLongitude,
            NearbyRadiusMeters,
            category,
            cancellationToken);

        return new NearbyPlacesResponse(station, places);
    }
}
