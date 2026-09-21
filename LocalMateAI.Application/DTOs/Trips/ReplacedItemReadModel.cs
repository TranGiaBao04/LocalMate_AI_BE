namespace LocalMateAI.Application.DTOs.Trips;

public sealed record ReplacedItemReadModel(
    Guid ItemId,
    Guid TripId,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    decimal EstimatedBudget);
