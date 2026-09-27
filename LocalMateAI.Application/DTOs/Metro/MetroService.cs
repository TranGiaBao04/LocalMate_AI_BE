namespace LocalMateAI.Application.DTOs.Metro;

/// <summary>Quy tắc chạy tàu của một chiều trong các thứ thuộc Days; giờ tính tại ga đầu tuyến của chiều đó.</summary>
public sealed record MetroService(
    IReadOnlyList<DayOfWeek> Days,
    MetroDirection Direction,
    TimeOnly FirstDeparture,
    TimeOnly LastDeparture,
    IReadOnlyList<MetroHeadway> Headways);
