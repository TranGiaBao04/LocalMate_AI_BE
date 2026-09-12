namespace LocalMateAI.Application.DTOs.Itineraries;

public sealed record CuratedItineraryResponse(
    Guid Id,
    string Title,
    string? Description,
    string? CoverImageUrl,
    int EstimatedDurationMinutes,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    IReadOnlyList<CuratedItineraryItemResponse> Items);
