namespace LocalMateAI.Application.DTOs.Itineraries;

public sealed record CuratedItineraryForApplyReadModel(
    Guid Id,
    string Title,
    int EstimatedDurationMinutes,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    IReadOnlyList<CuratedPlaceForApplyReadModel> Places);
