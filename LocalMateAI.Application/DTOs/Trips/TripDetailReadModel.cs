using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripDetailReadModel(
    Guid Id,
    TripStatus Status,
    double StartLatitude,
    double StartLongitude,
    string? StationName,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<TripItemReadModel> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? FinalizedAt);
