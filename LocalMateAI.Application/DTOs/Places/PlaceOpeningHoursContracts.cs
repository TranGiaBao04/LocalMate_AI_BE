namespace LocalMateAI.Application.DTOs.Places;

/// <summary>Giờ mở cửa của một thứ. Đóng cả ngày thì không có giờ; CloseTime &lt; OpenTime = mở qua nửa đêm.</summary>
public sealed record PlaceOpeningHourDto(DayOfWeek DayOfWeek, bool IsClosed, TimeOnly? OpenTime, TimeOnly? CloseTime);

/// <param name="HasData">false = địa điểm chưa có giờ mở cửa (mọi thứ đều trống).</param>
/// <param name="Days">Luôn đủ 7 thứ, Monday → Sunday; thứ chưa có dữ liệu thì IsClosed false và không có giờ.</param>
public sealed record PlaceOpeningHoursResponse(Guid PlaceId, bool HasData, IReadOnlyList<PlaceOpeningHourDto> Days);

/// <summary>Ghi đè giờ của cả tuần. Danh sách rỗng = xoá hết, về "chưa có dữ liệu".</summary>
public sealed record SavePlaceOpeningHoursRequest(IReadOnlyList<SavePlaceOpeningHourItem>? Days);

// Mọi field nullable để thiếu field nào báo lỗi đúng field đó.
public sealed record SavePlaceOpeningHourItem(DayOfWeek? DayOfWeek, bool? IsClosed, TimeOnly? OpenTime, TimeOnly? CloseTime);

public enum SavePlaceOpeningHoursResultStatus
{
    Success,
    ValidationFailed,
    NotFound
}

public sealed record SavePlaceOpeningHoursResult(
    SavePlaceOpeningHoursResultStatus Status,
    PlaceOpeningHoursResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
