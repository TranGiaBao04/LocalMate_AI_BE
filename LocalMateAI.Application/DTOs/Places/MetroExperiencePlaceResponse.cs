namespace LocalMateAI.Application.DTOs.Places;

public sealed record MetroExperiencePlaceResponse(
    Guid Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    double DistanceFromStationMeters);
