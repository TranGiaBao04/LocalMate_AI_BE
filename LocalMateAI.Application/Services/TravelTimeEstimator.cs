using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Ước tính khoảng cách/thời gian di chuyển giữa 2 toạ độ. Thuần. Tốc độ, hệ số đường vòng và ngưỡng đi bộ lấy từ
/// TripPlanningSettings (admin chỉnh); bỏ trống ⇒ TripPlanningSettings.Default.
/// </summary>
public static class TravelTimeEstimator
{
    private const double EarthRadiusKm = 6371.0;

    public static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Quãng đường bộ ước tính (km, làm tròn 2 số lẻ) = đường chim bay × hệ số đường vòng.</summary>
    public static double RoadDistanceKm(double lat1, double lng1, double lat2, double lng2,
        TripPlanningSettings? settings = null) =>
        Math.Round(HaversineKm(lat1, lng1, lat2, lng2) * (settings ?? TripPlanningSettings.Default).RoadDetourFactor, 2);

    public static int WalkingMinutes(double roadKm, TripPlanningSettings? settings = null) =>
        MinutesAt(roadKm, (settings ?? TripPlanningSettings.Default).WalkingSpeedKmH);

    public static int MotorbikeMinutes(double roadKm, TripPlanningSettings? settings = null) =>
        MinutesAt(roadKm, (settings ?? TripPlanningSettings.Default).MotorbikeSpeedKmH);

    public static int EstimateMinutes(double roadKm, TravelMode mode, TripPlanningSettings? settings = null)
    {
        var resolved = settings ?? TripPlanningSettings.Default;
        return mode switch
        {
            TravelMode.Walking => WalkingMinutes(roadKm, resolved),
            TravelMode.Motorbike => MotorbikeMinutes(roadKm, resolved),
            _ => roadKm * 1000 <= resolved.AutoWalkingMaxMeters
                ? WalkingMinutes(roadKm, resolved)
                : MotorbikeMinutes(roadKm, resolved)
        };
    }

    private static int MinutesAt(double roadKm, double speedKmH) =>
        (int)Math.Max(1, Math.Ceiling(roadKm / speedKmH * 60));

    private static double ToRadians(double angle) => Math.PI / 180.0 * angle;
}
