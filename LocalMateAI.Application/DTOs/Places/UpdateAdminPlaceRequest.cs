using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Places;

public sealed record UpdateAdminPlaceRequest(
    string? Name,
    string? Description,
    string? Address,
    double Latitude,
    double Longitude,
    PlaceCategory Category,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl);
