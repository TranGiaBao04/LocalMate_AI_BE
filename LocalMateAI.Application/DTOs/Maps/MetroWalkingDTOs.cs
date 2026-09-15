namespace LocalMateAI.Application.DTOs.Maps;

public record MetroStationInfo(
    string Code,
    string Name,
    double Latitude,
    double Longitude,
    string Address
);

public record MetroWalkingRouteResult(
    MetroStationInfo NearestStation,
    double DistanceKm,
    double DistanceMeters,
    int WalkingDurationMinutes,
    bool IsWalkable,
    string MapDirectionsUrl
);
