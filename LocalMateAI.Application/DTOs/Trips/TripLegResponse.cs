namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>
/// Cách đi tới một chặng: từ điểm xuất phát (với chặng đầu) hoặc từ chặng trước. Tính lại lúc đọc theo thông số và
/// lịch tàu hiện tại, nên TotalMinutes có thể lệch khoảng trống giờ đã lưu nếu admin đổi thông số sau khi tạo lịch.
/// Các field từ ToStationMode trở đi chỉ có giá trị khi Mode là "Metro".
/// </summary>
/// <param name="Mode">"Walking", "Motorbike" hoặc "Metro".</param>
/// <param name="Fallback">"metro_unavailable" khi đoạn này cần tàu nhưng đã hết chuyến (đổi sang xe máy đi thẳng).</param>
/// <param name="ToStationMode">Cách ra ga lên: "Walking" hoặc "Motorbike"; null khi đang đứng sẵn tại ga.</param>
/// <param name="WalkMinutes">Đi bộ từ ga xuống tới địa điểm.</param>
/// <param name="IsEstimated">Luôn true với đoạn đi tàu: giờ tàu là lịch dự kiến, không phải dữ liệu thời gian thực.</param>
public sealed record TripLegResponse(
    string Mode,
    int TotalMinutes,
    string? Fallback = null,
    string? ToStationMode = null,
    int? ToStationMinutes = null,
    StationRefDto? BoardStation = null,
    StationRefDto? AlightStation = null,
    int? WaitMinutes = null,
    int? RideMinutes = null,
    int? StopCount = null,
    int? WalkMinutes = null,
    bool? IsEstimated = null);
