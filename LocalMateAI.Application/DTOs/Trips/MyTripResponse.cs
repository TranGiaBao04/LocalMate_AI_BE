namespace LocalMateAI.Application.DTOs.Trips;

public sealed record MyTripResponse(
    Guid Id,
    string Status,
    double StartLatitude,
    double StartLongitude,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    int ItemCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);
