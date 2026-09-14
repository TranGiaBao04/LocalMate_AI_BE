namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripRequestDto(
    double StartLatitude,
    double StartLongitude,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    IReadOnlyList<Guid> TagIds);
