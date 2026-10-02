using LocalMateAI.Application.DTOs.Geo;

namespace LocalMateAI.Application.DTOs.Places;

public sealed record NearbyPlacesResponse(
    NearestStationResult Station,
    IReadOnlyList<PlaceSummaryResponse> Places);
