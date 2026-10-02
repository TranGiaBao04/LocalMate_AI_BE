namespace LocalMateAI.Application.DTOs.Matching;

public sealed record PlaceCandidateDto(
    Guid PlaceId,
    string PlaceName,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    Guid StationId,
    string StationName,
    int StationOrder,
    double DistanceFromStationMeters);