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
/// </summary>
public sealed class MetroDayTimetable
{
    private readonly IReadOnlyDictionary<MetroDirection, int[]> originDepartureMinutes;
    private readonly IReadOnlyDictionary<int, MetroStationOffset> offsets;

    private MetroDayTimetable(
        IReadOnlyDictionary<MetroDirection, int[]> originDepartureMinutes,
        IReadOnlyDictionary<int, MetroStationOffset> offsets)
    {
        this.originDepartureMinutes = originDepartureMinutes;
        this.offsets = offsets;
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
        timetable.StationOffsets.ToDictionary(offset => offset.StationOrder));

    /// <summary>
    /// Chuyến kế tiếp từ ga lên tới ga xuống cho người có mặt ở ga lên lúc <paramref name="minuteOfDay"/>
    /// (số phút tính từ 00:00 của ngày đi). Trả null khi đã hết tàu hoặc không có chuyến nào chạy chiều đó.
    /// </summary>
    public MetroRide? NextRide(int fromStationOrder, int toStationOrder, int minuteOfDay)
    {
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
                return new MetroRide(
                    atBoardStation - minuteOfDay,
                    minutesToAlightStation - minutesToBoardStation,
                    Math.Abs(toStationOrder - fromStationOrder));
            }
        }

        return null;
    }
}
