using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class PlaceQueryService(
    IPlaceRepository placeRepository,
    IGeoService geoService) : IPlaceQueryService
{
    private const double MetroClusterRadiusMeters = 800;
    private const double NearbyRadiusMeters = 800;

    public async Task<IReadOnlyList<MetroExperienceClusterResponse>> GetMetroClustersAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await placeRepository.GetMetroClusterPlacesAsync(
            MetroClusterRadiusMeters,
            cancellationToken);

        return rows
            .GroupBy(row => row.StationId)
            .Select(group =>
            {
                var station = group.First();
                var places = group
                    .OrderBy(row => row.DistanceFromStationMeters)
                    .ThenBy(row => row.PlaceName, StringComparer.Ordinal)
                    .ThenBy(row => row.PlaceId)
                    .Select(row => new MetroExperiencePlaceResponse(
                        row.PlaceId,
                        row.PlaceName,
                        row.PlaceAddress,
                        row.PlaceLatitude,
                        row.PlaceLongitude,
                        row.PlaceCategory,
                        row.EstimatedCostMin,
                        row.EstimatedCostMax,
                        row.ImageUrl,
                        row.DistanceFromStationMeters))
                    .ToList();

                return new MetroExperienceClusterResponse(
                    station.StationId,
                    station.StationName,
                    station.StationOrder,
                    station.StationLatitude,
                    station.StationLongitude,
                    places);
            })
            .OrderBy(cluster => cluster.StationOrder)
            .ThenBy(cluster => cluster.StationName, StringComparer.Ordinal)
            .ThenBy(cluster => cluster.StationId)
            .ToList();
    }

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

    public async Task<PlaceDetailResponse?> GetPlaceByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var place = await placeRepository.GetActiveByIdAsync(id, cancellationToken);
        if (place is null)
        {
            return null;
        }

        var nearestStation = await geoService.FindNearestStationForPlaceAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("No metro stations found.");

        return new PlaceDetailResponse(
            place.Id,
            place.Name,
            place.Description,
            place.Address,
            place.Latitude,
            place.Longitude,
            place.Category,
            place.Status,
            place.IsVerified,
            place.EstimatedCostMin,
            place.EstimatedCostMax,
            place.ImageUrl,
            place.Tags,
            nearestStation);
    }
}
