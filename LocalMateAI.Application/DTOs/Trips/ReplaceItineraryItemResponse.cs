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
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ItineraryTimelineItemResponse> Items); // mọi chặng của trip với giờ đã tính lại sau khi thay
