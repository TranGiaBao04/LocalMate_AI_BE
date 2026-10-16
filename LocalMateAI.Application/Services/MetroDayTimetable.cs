using LocalMateAI.Application.DTOs.Metro;

namespace LocalMateAI.Application.Services;

/// <summary>Một chuyến tàu giữa hai ga, tính từ lúc người đi có mặt ở ga lên.</summary>
/// <param name="WaitMinutes">Số phút chờ tới chuyến kế tiếp (0 nếu tới đúng lúc tàu chạy).</param>
/// <param name="RideMinutes">Số phút ngồi tàu từ ga lên tới ga xuống.</param>
/// <param name="StopCount">Số ga đi qua, tính cả ga xuống.</param>
public sealed record MetroRide(int WaitMinutes, int RideMinutes, int StopCount);

/// <summary>
/// Lịch tàu Metro của một ngày, dùng để tìm chuyến kế tiếp giữa hai ga khi xếp giờ lịch trình.
/// Thuần; dựng một lần mỗi request từ lịch đã nạp và kiểm tra lúc khởi động.
/// Giờ ≥ 24:00 tra lịch của ngày hôm sau (dựng khi cần).
/// </summary>
public sealed class MetroDayTimetable
{
    /// <summary>
    /// Chờ tàu lâu hơn mốc này thì coi như tàu không chạy (đổi phương tiện): bằng khoảng cách chuyến tối đa
    /// MetroTimetableValidator cho phép, nên ban ngày không bao giờ chạm tới.
    /// </summary>
    public const int MaxWaitMinutes = 60;

    private readonly IReadOnlyDictionary<MetroDirection, int[]> originDepartureMinutes;
    private readonly IReadOnlyDictionary<int, MetroStationOffset> offsets;
    private readonly Lazy<MetroDayTimetable>? nextDay;

    private MetroDayTimetable(
        IReadOnlyDictionary<MetroDirection, int[]> originDepartureMinutes,
        IReadOnlyDictionary<int, MetroStationOffset> offsets,
        Func<MetroDayTimetable>? nextDay)
    {
        this.originDepartureMinutes = originDepartureMinutes;
        this.offsets = offsets;
        this.nextDay = nextDay is null ? null : new Lazy<MetroDayTimetable>(nextDay);
    }

    public static MetroDayTimetable For(MetroTimetable timetable, DateOnly date) => new(
        // Lịch đã qua MetroTimetableValidator: mỗi chiều có đúng một service cho mỗi thứ trong tuần.
        timetable.Services
            .Where(service => service.Days.Contains(date.DayOfWeek))
            .ToDictionary(
                service => service.Direction,
                service => MetroDepartureGenerator.GenerateOriginDepartures(service)
                    .Select(departure => departure.Hour * 60 + departure.Minute)
                    .ToArray()),
        timetable.StationOffsets.ToDictionary(offset => offset.StationOrder),
        // Lịch ngày hôm sau chỉ dựng khi có đoạn đi sau nửa đêm.
        () => For(timetable, date.AddDays(1)));

    /// <summary>
    /// Chuyến kế tiếp từ ga lên tới ga xuống cho người có mặt ở ga lên lúc <paramref name="minuteOfDay"/>
    /// (số phút tính từ 00:00 của ngày đi; ≥ 1440 là ngày hôm sau).
    /// Trả null khi không có chuyến nào chạy chiều đó trong <see cref="MaxWaitMinutes"/> phút tới.
    /// </summary>
    public MetroRide? NextRide(int fromStationOrder, int toStationOrder, int minuteOfDay)
    {
        if (minuteOfDay >= TripClock.MinutesPerDay && nextDay is not null)
        {
            // Số phút chờ và ngồi tàu không phụ thuộc mốc ngày nên trả nguyên kết quả của ngày hôm sau.
            return nextDay.Value.NextRide(fromStationOrder, toStationOrder, minuteOfDay - TripClock.MinutesPerDay);
        }

        if (fromStationOrder == toStationOrder
            || !offsets.TryGetValue(fromStationOrder, out var from)
            || !offsets.TryGetValue(toStationOrder, out var to))
        {
            return null;
        }

        // Ga xuống có số thứ tự lớn hơn thì đi về phía Suối Tiên, ngược lại về phía Bến Thành.
        var direction = toStationOrder > fromStationOrder
            ? MetroDirection.TowardSuoiTien
            : MetroDirection.TowardBenThanh;

        if (!originDepartureMinutes.TryGetValue(direction, out var departures))
        {
            return null;
        }

        var minutesToBoardStation = MetroTimetableValidator.MinutesFromOrigin(from, direction);
        var minutesToAlightStation = MetroTimetableValidator.MinutesFromOrigin(to, direction);

        foreach (var departure in departures)
        {
            var atBoardStation = departure + minutesToBoardStation;
            if (atBoardStation >= minuteOfDay)
            {
                return atBoardStation - minuteOfDay > MaxWaitMinutes
                    ? null
                    : new MetroRide(
                        atBoardStation - minuteOfDay,
                        minutesToAlightStation - minutesToBoardStation,
                        Math.Abs(toStationOrder - fromStationOrder));
            }
        }

        return null;
    }

    /// <summary>
    /// Có chuyến nào (chiều nào cũng được) rời ga này trong <see cref="MaxWaitMinutes"/> phút kể từ
    /// <paramref name="minuteOfDay"/> không. Ga cuối của một chiều không có chuyến đi tiếp theo chiều đó.
    /// </summary>
    public bool IsRunningAt(int stationOrder, int minuteOfDay)
    {
        if (!offsets.TryGetValue(stationOrder, out var station))
        {
            return false;
        }

        var firstOrder = offsets.Keys.Min();
        var lastOrder = offsets.Keys.Max();

        return originDepartureMinutes.Any(pair =>
            !(pair.Key == MetroDirection.TowardSuoiTien && stationOrder == lastOrder)
            && !(pair.Key == MetroDirection.TowardBenThanh && stationOrder == firstOrder)
            && pair.Value
                .Select(departure => departure + MetroTimetableValidator.MinutesFromOrigin(station, pair.Key))
                .Any(atStation => atStation >= minuteOfDay && atStation - minuteOfDay <= MaxWaitMinutes));
    }
}
