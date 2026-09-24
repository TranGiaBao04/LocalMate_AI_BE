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
    int TotalDurationMinutes,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<TripItemResponse> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? FinalizedAt);
