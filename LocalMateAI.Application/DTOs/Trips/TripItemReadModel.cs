using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripItemReadModel(
    Guid ItemId,
    Guid PlaceId,
    string PlaceName,
    PlaceCategory Category,
    string? ImageUrl,
    double Latitude,
    double Longitude,
    string? StationName,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    decimal EstimatedBudget,
    string? Reasoning,
    bool IsVisited,
    DateTimeOffset? VisitedAt);
