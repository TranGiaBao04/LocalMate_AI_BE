using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record MyTripReadModel(
    Guid Id,
    TripStatus Status,
    double StartLatitude,
    double StartLongitude,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    int ItemCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? StationName,
    decimal EstimatedBudget,
    DateTime? FinalizedAt,
    DateTime? PlannedStartAt = null);
