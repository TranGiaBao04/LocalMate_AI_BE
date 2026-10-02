namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TimelineItemUpdate(
    Guid ItemId,
    int OrderIndex,
    TimeOnly ScheduledTime);
