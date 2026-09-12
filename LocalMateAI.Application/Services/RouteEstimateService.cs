namespace LocalMateAI.Application.Services;

public record RouteEstimateResult(
    double DistanceKm,
    double DistanceMeters,
    int WalkingDurationMinutes,
    int DrivingDurationMinutes,
    string DistanceText,
    string WalkingDurationText,
    string DrivingDurationText,
    string MapsDirectionsUrl
);

public interface IRouteEstimateService
{
    RouteEstimateResult EstimateRoute(
        double originLat, double originLng,
        double destLat, double destLng,
        string? originName = null, string? destName = null);
}

public sealed class RouteEstimateService(IGoogleMapsUrlBuilderService mapsUrlBuilder) : IRouteEstimateService
{
    private const double EarthRadiusKm = 6371.0;
    private const double WalkingSpeedKmH = 4.8; // ~80m/phút
    private const double DrivingSpeedKmH = 24.0; // Tốc độ trung bình nội thành TP.HCM

    public RouteEstimateResult EstimateRoute(
        double originLat, double originLng,
        double destLat, double destLng,
        string? originName = null, string? destName = null)
    {
        var distanceKm = CalculateHaversineDistance(originLat, originLng, destLat, destLng);
        
        // Điều chỉnh hệ số đường bộ thực tế (route detour factor ~1.3)
        var estimatedRoadDistanceKm = Math.Round(distanceKm * 1.3, 2);
        var distanceMeters = Math.Round(estimatedRoadDistanceKm * 1000, 0);

        var walkingMinutes = (int)Math.Max(1, Math.Ceiling((estimatedRoadDistanceKm / WalkingSpeedKmH) * 60));
        var drivingMinutes = (int)Math.Max(1, Math.Ceiling((estimatedRoadDistanceKm / DrivingSpeedKmH) * 60));

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

    private static double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double ToRadians(double angle) => (Math.PI / 180.0) * angle;
}
