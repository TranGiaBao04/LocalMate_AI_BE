using LocalMateAI.Application.DTOs.Metro;

namespace LocalMateAI.Tests;

/// <summary>Lịch Metro hợp lệ tối giản cho test: ga cách nhau 2 phút, mỗi chiều 2 nhóm ngày.</summary>
internal static class MetroTimetableTestData
{
    public static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    public static readonly DayOfWeek[] Weekend =
        [DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static MetroTimetable Valid() => new(
        EffectiveFrom: new DateOnly(2026, 9, 1),
        EffectiveTo: null,
        Source: "test",
        Precision: MetroTimetablePrecision.Headway,
        Notice: null,
        StationOffsets: Offsets(),
        Services:
        [
            Service(Weekdays, MetroDirection.TowardSuoiTien, new TimeOnly(22, 0)),
            Service(Weekdays, MetroDirection.TowardBenThanh, new TimeOnly(22, 0)),
            Service(Weekend, MetroDirection.TowardSuoiTien, new TimeOnly(23, 0)),
            Service(Weekend, MetroDirection.TowardBenThanh, new TimeOnly(23, 0))
        ]);

    // Ga n: cách Bến Thành 2*(n-1) phút, cách Suối Tiên 2*(14-n) phút.
    public static List<MetroStationOffset> Offsets() =>
        Enumerable.Range(1, 14)
            .Select(order => new MetroStationOffset(order, 2 * (order - 1), 2 * (14 - order)))
            .ToList();

    public static MetroService Service(IReadOnlyList<DayOfWeek> days, MetroDirection direction, TimeOnly last) => new(
        days,
        direction,
        new TimeOnly(5, 0),
        last,
        [
            new MetroHeadway(new TimeOnly(5, 0), new TimeOnly(7, 0), 10),
            new MetroHeadway(new TimeOnly(7, 0), last, 15)
        ]);
}
