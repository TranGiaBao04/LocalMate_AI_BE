using System.Text.Json.Serialization;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

/// <param name="Base">Tiêu chí của lịch người dùng đang xem. Có thì câu của người dùng được hiểu là yêu cầu
/// THAY ĐỔI so với lịch đó ("rẻ hơn", "ngắn hơn 1 tiếng"); không có thì là yêu cầu mới.</param>
public sealed record ParseTripRequestRequest(string? Text, ParsedTripFields? Base = null);

/// <summary>Các tiêu chí AI đọc được từ câu của người dùng. Tên trường trùng với TripRequestDto.</summary>
public sealed record ParsedTripFields(
    int? DurationHours,
    decimal? BudgetMax,
    IReadOnlyList<Guid> TagIds,
    TravelMode? TravelMode,
    int? StartStationOrder,
    int? DestinationStationOrder,
    DateOnly? PlannedDate,
    TimeOnly? StartTime,
    string? Note)
{
    public static readonly ParsedTripFields Empty = new(null, null, [], null, null, null, null, null, null);

    [JsonIgnore]
    public bool HasAnyValue =>
        HasTripCriteria || TravelMode is not null || StartStationOrder is not null
        || PlannedDate is not null || StartTime is not null;

    /// <summary>
    /// Có ít nhất một tiêu chí nói lên chuyến đi như thế nào (bao lâu, bao nhiêu tiền, thích gì, quanh đâu).
    /// Chỉ có ngày, giờ, phương tiện hay ga xuất phát thì chưa đủ để gợi ý lịch hợp ý.
    /// </summary>
    [JsonIgnore]
    public bool HasTripCriteria =>
        DurationHours is not null || BudgetMax is not null || TagIds.Count > 0
        || DestinationStationOrder is not null || Note is not null;
}

/// <param name="IsTripRequest">Người dùng có thể hiện ý muốn đi chơi hay không (AI xác định).</param>
/// <param name="Changed">Tên các trường khác với tiêu chí của lịch gốc; rỗng khi không có lịch gốc.</param>
public sealed record ParsedTripAnswer(bool IsTripRequest, ParsedTripFields Fields, IReadOnlyList<string> Changed);

/// <param name="Message">Câu hướng dẫn lịch sự khi chưa đủ để gợi ý lịch hợp ý: câu không phải yêu cầu đi chơi,
/// hoặc muốn đi chơi nhưng chưa nói chuyến đi như thế nào (chỉ có ngày, giờ, phương tiện). Ngược lại null.</param>
/// <param name="Missing">Những thứ form còn thiếu để tạo được lịch: durationHours, budgetMax, startLocation.</param>
/// <param name="Changed">Tên các trường vừa thay đổi so với Base (để FE tô sáng); rỗng khi không gửi Base.</param>
public sealed record ParseTripRequestResponse(
    bool IsTripRequest,
    string? Message,
    ParsedTripFields Fields,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Changed);

public enum ParseTripRequestResultStatus
{
    Success,
    InvalidText,
    UserNotFound,
    AiUnavailable,
    DailyLimitReached
}

public sealed record ParseTripRequestResult(
    ParseTripRequestResultStatus Status,
    ParseTripRequestResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    DateTime? ResetAtUtc = null);
