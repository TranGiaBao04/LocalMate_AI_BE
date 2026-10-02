namespace LocalMateAI.Application.DTOs.Trips;

public sealed record AlternativePlaceResponse(
    Guid PlaceId,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    string StationName,
    double DistanceFromStationMeters,
    double MatchScore);
