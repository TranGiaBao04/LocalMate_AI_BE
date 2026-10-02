using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record ReplaceItineraryItemResponse(
    Guid ItemId,
    Guid TripId,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    decimal EstimatedBudget,
    PlaceSummaryResponse Place,
    IReadOnlyList<string> Warnings);
