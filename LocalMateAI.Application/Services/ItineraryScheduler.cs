using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <param name="StationOrder">Ga mà địa điểm thuộc về; chỉ cần cho chế độ Metro (null = không đi tàu tới đây được).</param>
/// <param name="DistanceFromStationMeters">Đường chim bay từ địa điểm tới ga đó, để tính đoạn đi bộ ga ↔ địa điểm.</param>
public sealed record ScheduleInput(
    double Latitude,
    double Longitude,
    int DurationMinutes,
    decimal Cost = 0,
    int? StationOrder = null,
    double DistanceFromStationMeters = 0);

/// <param name="SourceIndex">Vị trí của chặng này trong danh sách đầu vào (vì Schedule có thể đổi thứ tự).</param>
public sealed record ScheduledSlot(int SourceIndex, TimeOnly ScheduledTime, int DurationMinutes);

/// <summary>Điểm xuất phát của chuyến đi, dùng để tính thời gian đi tới chặng đầu tiên.</summary>
/// <param name="Metro">Thông tin lên tàu; chỉ có khi phương tiện là Metro. Không có thì Metro được tính như Auto.</param>
public sealed record ScheduleOrigin(double Latitude, double Longitude, MetroBoarding? Metro = null);

/// <summary>Thông tin lên tàu của một chuyến đi chế độ Metro.</summary>
/// <param name="StationOrder">Ga lên: ga gần điểm xuất phát nhất, hoặc ga người dùng chọn xuất phát.</param>
/// <param name="DistanceToStationMeters">Đường chim bay từ điểm xuất phát tới ga lên; 0 khi xuất phát từ ga.</param>
/// <param name="StartsAtStation">Người dùng chọn xuất phát từ ga, nên không có đoạn ra ga.</param>
/// <param name="Timetable">Lịch tàu của ngày đi.</param>
public sealed record MetroBoarding(
    int StationOrder,
    double DistanceToStationMeters,
    bool StartsAtStation,
    MetroDayTimetable Timetable);

/// <summary>
/// Xếp giờ cho các chặng (BE-42). Thuần. Giờ là TimeOnly nên chuyến qua nửa đêm sẽ quay về 00:00 (chưa hỗ trợ ngày).
/// Đây là nơi duy nhất quyết định thời lượng và số chặng của một lịch trình.
/// </summary>
public static class ItineraryScheduler
{
    public static readonly TimeOnly DefaultStartTime = new(8, 0);
    public const int DefaultVisitMinutes = 90;

    // Thời gian tham quan theo loại do admin chỉnh (TripPlanningSettings); DefaultVisitMinutes chỉ cho loại lạ.
    public static int VisitMinutesFor(string category, TripPlanningSettings? settings = null)
    {
        var resolved = settings ?? TripPlanningSettings.Default;
        return category switch
        {
            nameof(PlaceCategory.Cafe) => resolved.CafeVisitMinutes,
            nameof(PlaceCategory.Food) => resolved.FoodVisitMinutes,
            nameof(PlaceCategory.Culture) => resolved.CultureVisitMinutes,
            nameof(PlaceCategory.CheckIn) => resolved.CheckInVisitMinutes,
            _ => DefaultVisitMinutes
        };
    }

    public static ScheduleInput ToScheduleInput(PlaceCandidateDto candidate, TripPlanningSettings? settings = null) =>
        new(candidate.Latitude, candidate.Longitude, VisitMinutesFor(candidate.Category, settings),
            candidate.EstimatedCostMax, candidate.StationOrder, candidate.DistanceFromStationMeters);

    /// <summary>
    /// Chọn và xếp giờ: duyệt theo thứ hạng, thêm địa điểm nếu tổng thời lượng (gồm đoạn đi từ điểm xuất phát tới
    /// chặng đầu) ≤ durationHours và tổng chi phí ≤ budgetMax, bỏ qua địa điểm không vừa rồi thử địa điểm sau.
    /// Mọi chặng, kể cả chặng hạng cao nhất, đều phải vừa; không chặng nào vừa thì trả danh sách rỗng.
    /// startTime là giờ RỜI điểm xuất phát: giờ chặng đầu = startTime + thời gian đi tới chặng đầu.
    /// Các chặng được chọn rồi sắp theo gần nhất, chặng đầu là chặng xếp hạng cao nhất trong số đó.
    /// Chế độ Metro (origin có Metro): gom chặng theo ga, ga gần ga lên đi trước; giờ gồm cả chờ tàu và ngồi tàu.
    /// </summary>
    public static IReadOnlyList<ScheduledSlot> Schedule(
        IReadOnlyList<ScheduleInput> rankedStops,
        TimeOnly startTime,
        int durationHours,
        TravelMode mode,
        decimal budgetMax,
        ScheduleOrigin? origin = null,
        TripPlanningSettings? settings = null)
    {
        var resolved = settings ?? TripPlanningSettings.Default;
        var limitMinutes = durationHours * 60;
        var selected = new List<int>();
        var spent = 0m;

        for (var candidate = 0; candidate < rankedStops.Count; candidate++)
        {
            if (spent + rankedStops[candidate].Cost > budgetMax)
            {
                continue;
            }

            var trial = selected.Append(candidate).Select(index => rankedStops[index]).ToList();
            var (trialOrder, trialOffsets) = Arrange(trial, startTime, mode, origin, resolved);
            if (trialOffsets[^1] + trial[trialOrder[^1]].DurationMinutes > limitMinutes)
            {
                continue;
            }

            selected.Add(candidate);
            spent += rankedStops[candidate].Cost;
        }

        if (selected.Count == 0)
        {
            return [];
        }

        var chosen = selected.Select(index => rankedStops[index]).ToList();
        var (order, offsets) = Arrange(chosen, startTime, mode, origin, resolved);

        return order
            .Select((chosenIndex, position) => new ScheduledSlot(
                selected[chosenIndex],
                startTime.AddMinutes(offsets[position]),
                chosen[chosenIndex].DurationMinutes))
            .ToList();
    }

    /// <summary>Dùng khi sửa lịch (xoá chặng...): giữ nguyên thứ tự và thời lượng, chỉ tính lại giờ.</summary>
    /// <param name="metroTimetable">Lịch tàu cho trip Metro khi không có origin (tính lại giờ từ chặng đầu còn lại).
    /// Có origin.Metro thì dùng lịch trong đó.</param>
    public static IReadOnlyList<ScheduledSlot> Reschedule(
        IReadOnlyList<ScheduleInput> orderedStops,
        TimeOnly startTime,
        TravelMode mode,
        ScheduleOrigin? origin = null,
        TripPlanningSettings? settings = null,
        MetroDayTimetable? metroTimetable = null)
    {
        var offsets = StartOffsets(
            orderedStops, startTime, mode, origin, settings ?? TripPlanningSettings.Default, metroTimetable);
        return orderedStops
            .Select((stop, index) => new ScheduledSlot(index, startTime.AddMinutes(offsets[index]), stop.DurationMinutes))
            .ToList();
    }

    /// <summary>
    /// Số phút từ lúc rời điểm xuất phát tới hết chặng cuối (gồm đoạn đi tới chặng đầu nếu có origin).
    /// Chế độ Metro phụ thuộc giờ tàu nên cần <paramref name="startTime"/>; bỏ trống thì lấy giờ bắt đầu mặc định.
    /// </summary>
    public static int TotalMinutes(IReadOnlyList<ScheduleInput> orderedStops, TravelMode mode, ScheduleOrigin? origin,
        TripPlanningSettings? settings = null, TimeOnly? startTime = null, MetroDayTimetable? metroTimetable = null)
    {
        if (orderedStops.Count == 0)
        {
            return 0;
        }

        var offsets = StartOffsets(
            orderedStops, startTime ?? DefaultStartTime, mode, origin, settings ?? TripPlanningSettings.Default,
            metroTimetable);
        return offsets[^1] + orderedStops[^1].DurationMinutes;
    }

    // Thứ tự đi và số phút bắt đầu từng chặng theo thứ tự đó.
    private static (List<int> Order, List<int> Offsets) Arrange(
        IReadOnlyList<ScheduleInput> stops, TimeOnly startTime, TravelMode mode, ScheduleOrigin? origin,
        TripPlanningSettings settings)
    {
        var order = mode == TravelMode.Metro && origin?.Metro is { } metro
            ? StationGroupedOrder(stops, metro.StationOrder)
            : NearestNeighborOrder(stops);
        return (order, StartOffsets(order.Select(index => stops[index]).ToList(), startTime, mode, origin, settings));
    }

    // Số phút từ lúc RỜI điểm xuất phát tới lúc bắt đầu từng chặng
    // = đi tới chặng đầu (nếu có origin) + tham quan các chặng trước + di chuyển giữa chúng.
    // Cần giờ bắt đầu thật vì đoạn đi tàu phải chờ chuyến kế tiếp theo lịch.
    private static List<int> StartOffsets(IReadOnlyList<ScheduleInput> stops, TimeOnly startTime, TravelMode mode,
        ScheduleOrigin? origin, TripPlanningSettings settings, MetroDayTimetable? metroTimetable = null)
    {
        var timetable = origin?.Metro?.Timetable ?? metroTimetable;
        var startMinuteOfDay = startTime.Hour * 60 + startTime.Minute;
        var offsets = new List<int>(stops.Count);
        var offset = 0;

        for (var i = 0; i < stops.Count; i++)
        {
            if (i == 0)
            {
                if (origin is not null)
                {
                    offset += TripLegPlanner.FromOrigin(origin, stops[0], startMinuteOfDay, mode, settings).TotalMinutes;
                }
            }
            else
            {
                offset += TripLegPlanner.BetweenStops(
                    stops[i - 1], stops[i], startMinuteOfDay + offset, mode, timetable, settings).TotalMinutes;
            }

            offsets.Add(offset);
            offset += stops[i].DurationMinutes;
        }

        return offsets;
    }

    // Chế độ Metro: đi hết các chặng của một ga rồi mới sang ga khác, để không lên xuống tàu qua lại.
    // Ga cách ga lên ít ga hơn đi trước (bằng nhau thì ga có thứ tự nhỏ hơn); chặng không rõ ga xếp cuối.
    // Trong một ga: bắt đầu từ địa điểm gần ga nhất, rồi tới địa điểm gần kế tiếp.
    private static List<int> StationGroupedOrder(IReadOnlyList<ScheduleInput> stops, int boardingStationOrder)
    {
        var order = new List<int>(stops.Count);
        var groups = Enumerable.Range(0, stops.Count)
            .GroupBy(index => stops[index].StationOrder)
            .OrderBy(group => group.Key is { } stationOrder ? Math.Abs(stationOrder - boardingStationOrder) : int.MaxValue)
            .ThenBy(group => group.Key);

        foreach (var group in groups)
        {
            var remaining = group.ToList();
            var current = remaining.MinBy(index => stops[index].DistanceFromStationMeters);
            remaining.Remove(current);
            order.Add(current);

            while (remaining.Count > 0)
            {
                var last = stops[current];
                current = remaining.MinBy(index => TravelTimeEstimator.HaversineKm(
                    last.Latitude, last.Longitude, stops[index].Latitude, stops[index].Longitude));
                remaining.Remove(current);
                order.Add(current);
            }
        }

        return order;
    }

    private static List<int> NearestNeighborOrder(IReadOnlyList<ScheduleInput> stops)
    {
        var order = new List<int> { 0 };
        var remaining = Enumerable.Range(1, stops.Count - 1).ToList();
        while (remaining.Count > 0)
        {
            var last = stops[order[^1]];
            var next = remaining.MinBy(index => TravelTimeEstimator.HaversineKm(
                last.Latitude, last.Longitude, stops[index].Latitude, stops[index].Longitude));
            order.Add(next);
            remaining.Remove(next);
        }

        return order;
    }
}
