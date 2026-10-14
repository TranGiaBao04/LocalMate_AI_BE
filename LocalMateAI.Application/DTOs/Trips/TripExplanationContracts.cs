using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripExplanationStopReadModel(
    Guid ItemId,
    Guid PlaceId,
    string PlaceName,
    PlaceCategory Category,
    string? Description,
    IReadOnlyList<string> TagNames,
    TimeOnly ScheduledTime,
    int EstimatedDurationMinutes);

public sealed record TripExplanationReadModel(
    Guid TripId,
    TripStatus Status,
    int DurationHours,
    string? Note,
    IReadOnlyList<string> InterestTagNames,
    IReadOnlyList<TripExplanationStopReadModel> Stops);

public sealed record TripExplanationUpdate(Guid ItemId, Guid PlaceId, string Reasoning);

public sealed record ExplainedItemResponse(Guid ItemId, Guid PlaceId, string Reasoning);

public sealed record ExplainTripResponse(IReadOnlyList<ExplainedItemResponse> Items, DateTime AiExplainedAt);

public enum ExplainTripResultStatus
{
    Success,
    InvalidId,
    UserNotFound,
    TripNotFound,
    TripFinalized,
    AiUnavailable,
    TripLimitReached,
    DailyLimitReached,
    CoolingDown
}

/// <param name="RetryAfterSeconds">Số giây còn phải chờ; có khi CoolingDown, hoặc khi AiUnavailable do nhà cung cấp lỗi.</param>
/// <param name="Charged">Lần gọi không dùng được này có tính vào trần ngày không; chỉ có nghĩa khi AiUnavailable.</param>
public sealed record ExplainTripResult(
    ExplainTripResultStatus Status,
    ExplainTripResponse? Response = null,
    DateTime? ResetAtUtc = null,
    int RetryAfterSeconds = 0,
    bool Charged = false);
