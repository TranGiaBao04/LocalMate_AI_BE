namespace LocalMateAI.Application.DTOs.Maps;

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
