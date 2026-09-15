using LocalMateAI.Application.DTOs.Maps;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class MetroWalkingRouter(
    IMetroStationRepository stationRepository,
    IGoogleMapsUrlBuilderService mapsUrlBuilder) : IMetroWalkingRouter
{
    public async Task<IReadOnlyList<MetroStationInfo>> GetMetroLine1StationsAsync(CancellationToken cancellationToken = default)
    {
        var stations = await stationRepository.GetAllAsync(cancellationToken);
        return stations.Select(s => new MetroStationInfo(
            Code: $"GA-{s.Order:D2}",
            Name: s.Name,
            Latitude: s.Latitude,
            Longitude: s.Longitude,
            Address: "Tuyến Metro số 1 Bến Thành - Suối Tiên"
        )).ToList().AsReadOnly();
    }

    public async Task<MetroWalkingRouteResult?> FindNearestMetroStationRouteAsync(
        double lat, double lng, string? placeName = null, CancellationToken cancellationToken = default)
    {
        var nearest = await stationRepository.FindNearestAsync(lat, lng, cancellationToken);
        if (nearest is null)
            return null;

        var roadDistanceKm = Math.Round(nearest.DistanceMeters / 1000.0 * 1.25, 2);
        var distanceMeters = Math.Round(nearest.DistanceMeters, 0);
        var walkingMinutes = (int)Math.Max(1, Math.Ceiling((roadDistanceKm / 4.8) * 60));

        var stationInfo = new MetroStationInfo(
            Code: "METRO-STATION",
            Name: nearest.StationName,
            Latitude: nearest.StationLatitude,
            Longitude: nearest.StationLongitude,
            Address: "Tuyến Metro số 1 Bến Thành - Suối Tiên"
        );

        var directionsUrl = mapsUrlBuilder.BuildDirectionsUrl(
            nearest.StationLatitude, nearest.StationLongitude,
            lat, lng,
            nearest.StationName, placeName,
            "walking");

        return new MetroWalkingRouteResult(
            NearestStation: stationInfo,
            DistanceKm: roadDistanceKm,
            DistanceMeters: distanceMeters,
            WalkingDurationMinutes: walkingMinutes,
            IsWalkable: roadDistanceKm <= 1.5,
            MapDirectionsUrl: directionsUrl
        );
    }
}
