namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripItemResponse(
    Guid ItemId,
    Guid PlaceId,
    string PlaceName,
    string Category,
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
    int? TravelMinutesFromPrevious, // suy ra từ khoảng trống giờ của lịch; null ở chặng đầu
    int? DistanceMetersFromPrevious,
    int? WalkingMinutes,
    int? MotorbikeMinutes,
    TripLegResponse? Leg = null, // cách đi tới chặng này; null khi không có đoạn đi (chặng đầu của trip không đặt giờ)
    string? GooglePlaceId = null); // mã địa điểm Google Maps; null thì FE mở bản đồ theo toạ độ
