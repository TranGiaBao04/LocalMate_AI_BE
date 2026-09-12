namespace LocalMateAI.Application.DTOs.Places;

public sealed record MetroClusterPlaceReadModel(
    Guid StationId,
    string StationName,
    int StationOrder,
    double StationLatitude,
    double StationLongitude,
    Guid PlaceId,
    string PlaceName,
    string PlaceAddress,
    double PlaceLatitude,
    double PlaceLongitude,
    string PlaceCategory,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    double DistanceFromStationMeters);
