using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Itineraries;

public sealed record CuratedPlaceForApplyReadModel(
    Guid PlaceId,
    int OrderIndex,
    double Latitude,
    double Longitude,
    decimal EstimatedCostMax,
    PlaceCategory Category);
