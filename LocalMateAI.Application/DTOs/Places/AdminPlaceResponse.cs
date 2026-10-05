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
    bool IsVerified,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? GooglePlaceId = null); // mã địa điểm Google Maps; null thì FE mở bản đồ theo toạ độ
