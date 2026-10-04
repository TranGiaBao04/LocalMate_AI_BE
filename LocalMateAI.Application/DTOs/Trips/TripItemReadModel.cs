using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripItemReadModel(
    Guid ItemId,
    Guid PlaceId,
    string PlaceName,
    PlaceCategory Category,
    string? ImageUrl,
    double Latitude,
    double Longitude,
    string? StationName,
    int OrderIndex,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes,
    decimal EstimatedBudget,
    string? Reasoning,
    bool IsVisited,
    DateTimeOffset? VisitedAt,
    string? Address = null, // chỉ dùng nội bộ (mail lịch trình), không có trong TripItemResponse
    int? StationOrder = null, // ga gần địa điểm nhất (cùng ga với StationName)
    double? DistanceFromStationMeters = null); // đường chim bay từ địa điểm tới ga đó
