namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>Một chặng dừng, chỉ gồm các trường cần để tính tổng.</summary>
public sealed record TripStopSnapshot(
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    decimal EstimatedBudget);

public sealed record TripTotals(
    decimal TotalBudget,
    int TotalVisitMinutes,
    int TotalTravelMinutes,
    int TotalMinutes,
    TimeOnly? EndTime);
