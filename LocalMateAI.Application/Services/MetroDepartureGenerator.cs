using LocalMateAI.Application.DTOs.Metro;

namespace LocalMateAI.Application.Services;

/// <summary>Sinh giờ tàu từ quy tắc trong metro-timetable.json. Giả định lịch đã qua MetroTimetableValidator.</summary>
public static class MetroDepartureGenerator
{
    /// <summary>
    /// Giờ rời ga đầu tuyến: từ chuyến đầu, cộng số phút của khung chứa giờ hiện tại, dừng trước chuyến cuối,
    /// rồi luôn thêm đúng chuyến cuối (giờ công bố cố định).
    /// </summary>
    public static IReadOnlyList<TimeOnly> GenerateOriginDepartures(MetroService service)
    {
        // Tính bằng số phút (không cộng TimeOnly vì nó quay vòng qua nửa đêm).
        var lastMinutes = ToMinutes(service.LastDeparture);
        var current = ToMinutes(service.FirstDeparture);
        var departures = new List<TimeOnly>();

        while (current < lastMinutes)
        {
            departures.Add(FromMinutes(current));
            current += FindHeadway(service.Headways, current).Minutes;
        }

        departures.Add(service.LastDeparture);
        return departures;
    }

    /// <summary>Giờ tàu tại ga = giờ rời ga đầu tuyến + số phút tàu chạy tới ga.</summary>
    public static IReadOnlyList<TimeOnly> ShiftToStation(IReadOnlyList<TimeOnly> originDepartures, int offsetMinutes) =>
        originDepartures.Select(departure => departure.AddMinutes(offsetMinutes)).ToList();

    /// <summary>Dời các khung giờ tới ga; khung cuối cắt tại chuyến cuối để không vượt 24:00 khi cộng số phút.</summary>
    public static IReadOnlyList<MetroHeadway> ShiftHeadwaysToStation(MetroService service, int offsetMinutes) =>
        service.Headways
            .Select(headway => new MetroHeadway(
                headway.From.AddMinutes(offsetMinutes),
                Min(headway.To, service.LastDeparture).AddMinutes(offsetMinutes),
                headway.Minutes))
            .ToList();

    private static MetroHeadway FindHeadway(IReadOnlyList<MetroHeadway> headways, int minutes) =>
        headways.First(headway => ToMinutes(headway.From) <= minutes && minutes < ToMinutes(headway.To));

    private static TimeOnly Min(TimeOnly left, TimeOnly right) => left < right ? left : right;

    private static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    private static TimeOnly FromMinutes(int minutes) => new(minutes / 60, minutes % 60);
}
