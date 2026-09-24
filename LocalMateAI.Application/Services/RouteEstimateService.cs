using LocalMateAI.Application.DTOs.Maps;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class RouteEstimateService(IGoogleMapsUrlBuilderService mapsUrlBuilder) : IRouteEstimateService
{
    public RouteEstimateResult EstimateRoute(
        double originLat, double originLng,
        double destLat, double destLng,
        string? originName = null, string? destName = null)
    {
        // Đã gồm hệ số đường bộ thực tế (route detour factor ~1.3)
        var estimatedRoadDistanceKm = TravelTimeEstimator.RoadDistanceKm(originLat, originLng, destLat, destLng);
        var distanceMeters = Math.Round(estimatedRoadDistanceKm * 1000, 0);

        var walkingMinutes = TravelTimeEstimator.WalkingMinutes(estimatedRoadDistanceKm);
        var drivingMinutes = TravelTimeEstimator.MotorbikeMinutes(estimatedRoadDistanceKm);

        var mapsUrl = mapsUrlBuilder.BuildDirectionsUrl(
            originLat, originLng, destLat, destLng, originName, destName, "walking");

        return new RouteEstimateResult(
            DistanceKm: estimatedRoadDistanceKm,
            DistanceMeters: distanceMeters,
            WalkingDurationMinutes: walkingMinutes,
            DrivingDurationMinutes: drivingMinutes,
            DistanceText: estimatedRoadDistanceKm < 1.0 ? $"{distanceMeters} m" : $"{estimatedRoadDistanceKm:F1} km",
            WalkingDurationText: $"{walkingMinutes} phút đi bộ",
            DrivingDurationText: $"{drivingMinutes} phút xe máy/ô tô",
            MapsDirectionsUrl: mapsUrl
        );
    }

}
