namespace LocalMateAI.Application.DTOs.Metro;

/// <summary>
/// Lịch tàu Metro số 1 do team tự thiết kế theo quy tắc (không phải dữ liệu thời gian thực của HURC1),
/// nạp từ metro-timetable.json nhúng trong Infrastructure.
/// </summary>
public sealed record MetroTimetable(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Source,
    MetroTimetablePrecision Precision,
    string? Notice,
    IReadOnlyList<MetroStationOffset> StationOffsets,
    IReadOnlyList<MetroService> Services);
