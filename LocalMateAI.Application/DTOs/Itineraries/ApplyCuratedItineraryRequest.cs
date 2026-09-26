using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Itineraries;

/// <summary>
/// Tuỳ chọn khi áp dụng lịch trình mẫu thành bản nháp.
/// </summary>
/// <param name="StartLatitude">Vĩ độ điểm xuất phát; phải gửi cùng <paramref name="StartLongitude"/>. Bỏ trống thì dùng toạ độ địa điểm đầu tiên.</param>
/// <param name="StartLongitude">Kinh độ điểm xuất phát.</param>
/// <param name="StartTime">Giờ RỜI điểm xuất phát (giờ Việt Nam); chặng đầu bắt đầu sau khi cộng thời gian đi tới đó. Bỏ trống thì 08:00.</param>
/// <param name="PlannedDate">Ngày đi (giờ Việt Nam). Bỏ trống = hôm nay.</param>
/// <param name="TravelMode">Phương tiện tính thời gian di chuyển. Bỏ trống = Auto.</param>
public sealed record ApplyCuratedItineraryRequest(
    double? StartLatitude,
    double? StartLongitude,
    TimeOnly? StartTime,
    DateOnly? PlannedDate = null,
    TravelMode? TravelMode = null);
