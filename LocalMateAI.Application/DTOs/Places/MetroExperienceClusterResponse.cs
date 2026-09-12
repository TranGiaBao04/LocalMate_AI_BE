namespace LocalMateAI.Application.DTOs.Places;

public sealed record MetroExperienceClusterResponse(
    Guid StationId,
    string StationName,
    int StationOrder,
    double StationLatitude,
    double StationLongitude,
    IReadOnlyList<MetroExperiencePlaceResponse> Places);
