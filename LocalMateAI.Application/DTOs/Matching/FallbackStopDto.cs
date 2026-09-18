namespace LocalMateAI.Application.DTOs.Matching;

public sealed record FallbackStopDto(
    Guid PlaceId,
    string PlaceName,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    decimal EstimatedBudget,
    string Reasoning,
    double MatchScore,
    double DistanceFromStationMeters,
    string StationName);