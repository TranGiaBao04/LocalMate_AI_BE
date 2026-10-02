namespace LocalMateAI.Application.DTOs.Itineraries;

public sealed record CuratedItineraryItemResponse(
    Guid PlaceId,
    string PlaceName,
    int OrderIndex);
