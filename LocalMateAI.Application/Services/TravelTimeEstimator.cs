using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>Ước tính khoảng cách/thời gian di chuyển giữa 2 toạ độ. Thuần, không phụ thuộc DI.</summary>
public static class TravelTimeEstimator
{
    public const double RoadDetourFactor = 1.3;
    public const double WalkingSpeedKmH = 4.8; // ~80 m/phút
    public const double MotorbikeSpeedKmH = 24.0; // tốc độ trung bình nội thành TP.HCM
    public const double MaxAutoWalkingMeters = 700;
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
    public static double RoadDistanceKm(double lat1, double lng1, double lat2, double lng2) =>
        Math.Round(HaversineKm(lat1, lng1, lat2, lng2) * RoadDetourFactor, 2);

    public static int WalkingMinutes(double roadKm) => MinutesAt(roadKm, WalkingSpeedKmH);

    public static int MotorbikeMinutes(double roadKm) => MinutesAt(roadKm, MotorbikeSpeedKmH);

    public static int EstimateMinutes(double roadKm, TravelMode mode) => mode switch
    {
        TravelMode.Walking => WalkingMinutes(roadKm),
        TravelMode.Motorbike => MotorbikeMinutes(roadKm),
        _ => roadKm * 1000 <= MaxAutoWalkingMeters ? WalkingMinutes(roadKm) : MotorbikeMinutes(roadKm)
    };

    private static int MinutesAt(double roadKm, double speedKmH) =>
        (int)Math.Max(1, Math.Ceiling(roadKm / speedKmH * 60));

    private static double ToRadians(double angle) => Math.PI / 180.0 * angle;
}
