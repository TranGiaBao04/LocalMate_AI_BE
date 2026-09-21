namespace LocalMateAI.Application.DTOs.Trips;

public sealed record ItineraryTimelineItemResponse(
    Guid ItemId,
    Guid PlaceId,
    string PlaceName,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    decimal EstimatedBudget);
