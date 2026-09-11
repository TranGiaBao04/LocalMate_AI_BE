namespace LocalMateAI.Application.DTOs.Geo;

public sealed record NearestStationResult(
    Guid StationId,
    string StationName,
    double StationLatitude,
    double StationLongitude,
    double DistanceMeters);
