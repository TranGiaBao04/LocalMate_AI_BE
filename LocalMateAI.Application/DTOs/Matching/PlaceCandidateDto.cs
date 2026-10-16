using System.Text.Json.Serialization;
using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.DTOs.Matching;

/// <param name="OpeningHours">Giờ mở cửa theo thứ, nạp ở MetroClusterMatchingService; null = chưa nạp (không ràng buộc
/// giờ), rỗng = chưa có dữ liệu. Không trả ra JSON (/match trả nguyên ứng viên).</param>
public sealed record PlaceCandidateDto(
    Guid PlaceId,
    string PlaceName,
    string Address,
    double Latitude,
    double Longitude,
    string Category,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? ImageUrl,
    Guid StationId,
    string StationName,
    int StationOrder,
    double DistanceFromStationMeters,
    string? Description = null,
    [property: JsonIgnore] IReadOnlyList<PlaceOpeningHourDto>? OpeningHours = null);
