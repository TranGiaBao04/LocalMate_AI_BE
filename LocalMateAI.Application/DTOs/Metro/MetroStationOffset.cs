namespace LocalMateAI.Application.DTOs.Metro;

/// <summary>Số phút tàu chạy từ ga đầu tuyến của mỗi chiều tới ga này (Bến Thành cho TowardSuoiTien, Suối Tiên cho TowardBenThanh).</summary>
public sealed record MetroStationOffset(
    int StationOrder,
    int MinutesTowardSuoiTien,
    int MinutesTowardBenThanh);
