using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Metro;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class MetroTimetableService(
    IMetroTimetableSource timetableSource,
    IMasterDataService masterDataService,
    TimeProvider clock) : IMetroTimetableService
{
    public async Task<MetroStationDeparturesResult> GetStationDeparturesAsync(
        int stationOrder,
        DateOnly? date,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(VietnamTime.Now(clock));
        var dateError = ValidateDate(date, today);
        if (dateError is not null)
        {
            return MetroStationDeparturesResult.Invalid(
                new Dictionary<string, string[]> { ["date"] = [dateError] });
        }

        // Tên ga lấy từ danh sách đã cache sẵn của master-data (không truy vấn DB mỗi request).
        var stations = (await masterDataService.GetMasterDataAsync(cancellationToken)).MetroStations;
        var station = stations.FirstOrDefault(item => item.Order == stationOrder);
        if (station is null)
        {
            return MetroStationDeparturesResult.MissingStation();
        }

        var timetable = timetableSource.Timetable;
        var targetDate = date ?? today;
        var offset = timetable.StationOffsets.Single(item => item.StationOrder == stationOrder);

        var directions = timetable.Services
            .Where(service => service.Days.Contains(targetDate.DayOfWeek) && !IsTerminus(stationOrder, service.Direction))
            .OrderBy(service => service.Direction)
            .Select(service =>
            {
                var minutes = MetroTimetableValidator.MinutesFromOrigin(offset, service.Direction);
                return new MetroDirectionDeparturesResponse(
                    service.Direction,
                    TerminusName(stations, service.Direction),
                    MetroDepartureGenerator.ShiftToStation(
                        MetroDepartureGenerator.GenerateOriginDepartures(service), minutes),
                    MetroDepartureGenerator.ShiftHeadwaysToStation(service, minutes));
            })
            .ToList();

        var allDepartures = directions.SelectMany(direction => direction.Departures).ToList();

        return MetroStationDeparturesResult.Succeeded(new MetroStationDeparturesResponse(
            new MetroStationRefResponse(station.Order, station.Name),
            targetDate,
            allDepartures.Count == 0 ? null : allDepartures.Min(),
            allDepartures.Count == 0 ? null : allDepartures.Max(),
            IsEstimated: true,
            timetable.Precision,
            IsWithinEffectivePeriod(timetable, targetDate),
            timetable.EffectiveFrom,
            timetable.EffectiveTo,
            timetable.Notice,
            directions));
    }

    public async Task<MetroJourneyResult> GetJourneyAsync(
        int fromStationOrder,
        int toStationOrder,
        DateOnly? date,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(VietnamTime.Now(clock));
        var errors = new Dictionary<string, string[]>();

        var dateError = ValidateDate(date, today);
        if (dateError is not null)
        {
            errors["date"] = [dateError];
        }

        if (fromStationOrder == toStationOrder)
        {
            errors["to"] = ["Ga đến phải khác ga đi."];
        }

        if (errors.Count > 0)
        {
            return MetroJourneyResult.Invalid(errors);
        }

        var stations = (await masterDataService.GetMasterDataAsync(cancellationToken)).MetroStations;
        var from = stations.FirstOrDefault(item => item.Order == fromStationOrder);
        var to = stations.FirstOrDefault(item => item.Order == toStationOrder);
        if (from is null || to is null)
        {
            return MetroJourneyResult.MissingStation();
        }

        var timetable = timetableSource.Timetable;
        var targetDate = date ?? today;

        // Ga đến có số thứ tự lớn hơn thì đi về phía Suối Tiên, ngược lại về phía Bến Thành.
        var direction = toStationOrder > fromStationOrder
            ? MetroDirection.TowardSuoiTien
            : MetroDirection.TowardBenThanh;

        // Luật "mỗi chiều phủ đủ 7 ngày, mỗi ngày đúng 1 service" bảo đảm luôn có đúng 1 service.
        var service = timetable.Services.Single(item =>
            item.Direction == direction && item.Days.Contains(targetDate.DayOfWeek));

        var fromMinutes = MinutesFromOrigin(timetable, fromStationOrder, direction);
        var toMinutes = MinutesFromOrigin(timetable, toStationOrder, direction);

        var trips = MetroDepartureGenerator.GenerateOriginDepartures(service)
            .Select(departure => new MetroJourneyTripResponse(
                departure.AddMinutes(fromMinutes),
                departure.AddMinutes(toMinutes)))
            .ToList();

        return MetroJourneyResult.Succeeded(new MetroJourneyResponse(
            new MetroStationRefResponse(from.Order, from.Name),
            new MetroStationRefResponse(to.Order, to.Name),
            targetDate,
            direction,
            TerminusName(stations, direction),
            TravelMinutes: toMinutes - fromMinutes,
            StopCount: Math.Abs(toStationOrder - fromStationOrder),
            IsEstimated: true,
            timetable.Precision,
            IsWithinEffectivePeriod(timetable, targetDate),
            timetable.EffectiveFrom,
            timetable.EffectiveTo,
            timetable.Notice,
            trips,
            MetroDepartureGenerator.ShiftHeadwaysToStation(service, fromMinutes)));
    }

    /// <summary>date không ở quá khứ và không quá 90 ngày (cùng giới hạn với plannedDate của trip). Null = hôm nay.</summary>
    private static string? ValidateDate(DateOnly? date, DateOnly today)
    {
        if (date is null)
        {
            return null;
        }

        if (date < today)
        {
            return "Ngày xem lịch tàu không được ở quá khứ.";
        }

        return date > today.AddDays(TripTimingRules.MaxDaysAhead)
            ? $"Ngày xem lịch tàu không được quá {TripTimingRules.MaxDaysAhead} ngày kể từ hôm nay."
            : null;
    }

    private static bool IsWithinEffectivePeriod(MetroTimetable timetable, DateOnly date) =>
        date >= timetable.EffectiveFrom && (timetable.EffectiveTo is null || date <= timetable.EffectiveTo);

    private static int MinutesFromOrigin(MetroTimetable timetable, int stationOrder, MetroDirection direction) =>
        MetroTimetableValidator.MinutesFromOrigin(
            timetable.StationOffsets.Single(item => item.StationOrder == stationOrder),
            direction);

    // Ga cuối của một chiều không có tàu đi tiếp theo chiều đó.
    private static bool IsTerminus(int stationOrder, MetroDirection direction) =>
        direction == MetroDirection.TowardSuoiTien
            ? stationOrder == MetroTimetableValidator.LastStationOrder
            : stationOrder == MetroTimetableValidator.FirstStationOrder;

    private static string TerminusName(IReadOnlyList<MetroStationSummaryResponse> stations, MetroDirection direction)
    {
        var terminusOrder = direction == MetroDirection.TowardSuoiTien
            ? MetroTimetableValidator.LastStationOrder
            : MetroTimetableValidator.FirstStationOrder;

        return stations.First(item => item.Order == terminusOrder).Name;
    }
}
