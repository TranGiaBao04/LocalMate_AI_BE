using LocalMateAI.Application.DTOs.Maps;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class RouteEstimateService(
    IGoogleMapsUrlBuilderService mapsUrlBuilder,
    ISystemSettingProvider settings) : IRouteEstimateService
{
    public async Task<RouteEstimateResult> EstimateRouteAsync(
        double originLat, double originLng,
        double destLat, double destLng,
        string? originName = null, string? destName = null,
        CancellationToken cancellationToken = default)
    {
        // Hệ số đường vòng và tốc độ do admin chỉnh (SystemSettings), cùng bộ số với xếp lịch.
        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);
        var estimatedRoadDistanceKm = TravelTimeEstimator.RoadDistanceKm(
            originLat, originLng, destLat, destLng, planning);
        var distanceMeters = Math.Round(estimatedRoadDistanceKm * 1000, 0);

        var walkingMinutes = TravelTimeEstimator.WalkingMinutes(estimatedRoadDistanceKm, planning);
        var drivingMinutes = TravelTimeEstimator.MotorbikeMinutes(estimatedRoadDistanceKm, planning);

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
