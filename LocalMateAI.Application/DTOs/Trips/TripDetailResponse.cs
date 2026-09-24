namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripDetailResponse(
    Guid Id,
    string Status,
    double StartLatitude,
    double StartLongitude,
    string? StationName,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    decimal EstimatedBudget,
    int TotalDurationMinutes, // giữ nguyên nghĩa cũ: chỉ tổng thời gian tham quan
    int TotalVisitMinutes,
    int TotalTravelMinutes,
    int TotalMinutes, // tham quan + di chuyển
    TimeOnly? EndTime,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<TripItemResponse> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? FinalizedAt);
