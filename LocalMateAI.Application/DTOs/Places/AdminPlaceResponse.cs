namespace LocalMateAI.Application.DTOs.Places;

public sealed record AdminPlaceResponse(
    Guid Id,
    string Name,
    string? Description,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    string Status,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt);
