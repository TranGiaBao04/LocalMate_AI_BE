namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>Một ga Metro trong response của luồng tạo lịch. Order (1–14) là khoá FE dùng để chọn ga.</summary>
public sealed record StationRefDto(int Order, string Name);

/// <summary>
/// Ga gợi ý khi khu người dùng chọn chưa có địa điểm. PlaceCount tính cả các ga kề và chưa lọc theo ngân sách.
/// </summary>
public sealed record SuggestedStationDto(int Order, string Name, int PlaceCount);
