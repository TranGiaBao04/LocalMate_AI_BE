using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Places;

/// <summary>
/// <c>EstimatedCostMin</c>/<c>EstimatedCostMax</c> là số nguyên VNĐ đầy đủ (30000 = 30.000đ, không phải 30).
/// </summary>
public sealed record CreateAdminPlaceRequest(
    string? Name,
    string? Description,
    string? Address,
    double Latitude,
    double Longitude,
    PlaceCategory Category,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl);
