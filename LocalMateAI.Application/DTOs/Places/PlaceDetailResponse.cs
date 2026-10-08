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
    NearestStationResult NearestStation,
    string? GooglePlaceId = null, // mã địa điểm Google Maps; null thì FE mở bản đồ theo toạ độ
    decimal? AverageRating = null, // làm tròn 1 chữ số; null khi chưa có đánh giá nào
    int ReviewCount = 0);
