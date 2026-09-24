namespace LocalMateAI.Application.DTOs.Itineraries;

/// <summary>
/// Tuỳ chọn khi áp dụng lịch trình mẫu thành bản nháp.
/// </summary>
/// <param name="StartLatitude">Vĩ độ điểm xuất phát; phải gửi cùng <paramref name="StartLongitude"/>. Bỏ trống thì dùng toạ độ địa điểm đầu tiên.</param>
/// <param name="StartLongitude">Kinh độ điểm xuất phát.</param>
/// <param name="StartTime">Giờ bắt đầu chặng đầu tiên. Bỏ trống thì 08:00.</param>
public sealed record ApplyCuratedItineraryRequest(
    double? StartLatitude,
    double? StartLongitude,
    TimeOnly? StartTime);
