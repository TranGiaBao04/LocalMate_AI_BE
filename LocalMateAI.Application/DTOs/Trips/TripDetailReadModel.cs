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
    DateTime? FinalizedAt,
    TravelMode TravelMode = TravelMode.Auto,
    DateTime? PlannedStartAt = null,
    int? StationOrder = null, // ga lên: ga gần điểm xuất phát nhất (cùng ga với StationName)
    double? DistanceToStationMeters = null, // đường chim bay từ điểm xuất phát tới ga lên
    StationRefDto? StartStation = null,
    StationRefDto? DestinationStation = null,
    string? Note = null,
    bool NoteApplied = false);
