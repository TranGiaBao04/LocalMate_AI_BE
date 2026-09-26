namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TimelineItemSnapshot(
    Guid ItemId,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    double Latitude,
    double Longitude);
