namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TimelineItemSnapshot(
    Guid ItemId,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    double Latitude,
    double Longitude,
    int? StationOrder = null, // chỉ cần cho trip Metro: ga gần địa điểm nhất
    double DistanceFromStationMeters = 0);
