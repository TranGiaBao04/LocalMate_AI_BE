using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Tags;

namespace LocalMateAI.Application.DTOs.Places;

public sealed record PlaceDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    string Status,
    bool IsVerified,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    IReadOnlyList<TagResponse> Tags,
    NearestStationResult NearestStation);
