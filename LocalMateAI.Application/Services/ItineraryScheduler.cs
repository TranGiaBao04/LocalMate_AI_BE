using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed record ScheduleInput(double Latitude, double Longitude, int DurationMinutes, decimal Cost = 0);

/// <param name="SourceIndex">Vị trí của chặng này trong danh sách đầu vào (vì Schedule có thể đổi thứ tự).</param>
public sealed record ScheduledSlot(int SourceIndex, TimeOnly ScheduledTime, int DurationMinutes);

/// <summary>Điểm xuất phát của chuyến đi, dùng để tính thời gian đi tới chặng đầu tiên.</summary>
public sealed record ScheduleOrigin(double Latitude, double Longitude);

/// <summary>
/// Xếp giờ cho các chặng (BE-42). Thuần. Giờ là TimeOnly nên chuyến qua nửa đêm sẽ quay về 00:00 (chưa hỗ trợ ngày).
/// Đây là nơi duy nhất quyết định thời lượng và số chặng của một lịch trình.
/// </summary>
public static class ItineraryScheduler
{
    public static readonly TimeOnly DefaultStartTime = new(8, 0);
    public const int DefaultVisitMinutes = 90;

    public static int VisitMinutesFor(string category) => category switch
    {
        nameof(PlaceCategory.Cafe) => 60,
        nameof(PlaceCategory.Food) => 75,
        nameof(PlaceCategory.Culture) => 90,
        nameof(PlaceCategory.CheckIn) => 45,
        _ => DefaultVisitMinutes
    };

    public static ScheduleInput ToScheduleInput(PlaceCandidateDto candidate) =>
        new(candidate.Latitude, candidate.Longitude, VisitMinutesFor(candidate.Category), candidate.EstimatedCostMax);

    /// <summary>
    /// Chọn và xếp giờ: duyệt theo thứ hạng, thêm địa điểm nếu tổng thời lượng (gồm đoạn đi từ điểm xuất phát tới
    /// chặng đầu) ≤ durationHours và tổng chi phí ≤ budgetMax, bỏ qua địa điểm không vừa rồi thử địa điểm sau.
    /// Mọi chặng, kể cả chặng hạng cao nhất, đều phải vừa; không chặng nào vừa thì trả danh sách rỗng.
    /// startTime là giờ RỜI điểm xuất phát: giờ chặng đầu = startTime + thời gian đi tới chặng đầu.
    /// Các chặng được chọn rồi sắp theo gần nhất, chặng đầu là chặng xếp hạng cao nhất trong số đó.
    /// </summary>
    public static IReadOnlyList<ScheduledSlot> Schedule(
        IReadOnlyList<ScheduleInput> rankedStops,
        TimeOnly startTime,
        int durationHours,
        TravelMode mode,
        decimal budgetMax,
        ScheduleOrigin? origin = null)
    {
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
            var (trialOrder, trialOffsets) = Arrange(trial, mode, origin);
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
        var (order, offsets) = Arrange(chosen, mode, origin);

        return order
            .Select((chosenIndex, position) => new ScheduledSlot(
                selected[chosenIndex],
                startTime.AddMinutes(offsets[position]),
                chosen[chosenIndex].DurationMinutes))
            .ToList();
    }

    /// <summary>Dùng khi sửa lịch (xoá chặng...): giữ nguyên thứ tự và thời lượng, chỉ tính lại giờ.</summary>
    public static IReadOnlyList<ScheduledSlot> Reschedule(
        IReadOnlyList<ScheduleInput> orderedStops,
        TimeOnly startTime,
        TravelMode mode,
        ScheduleOrigin? origin = null)
    {
        var offsets = StartOffsets(orderedStops, mode, origin);
        return orderedStops
            .Select((stop, index) => new ScheduledSlot(index, startTime.AddMinutes(offsets[index]), stop.DurationMinutes))
            .ToList();
    }

    /// <summary>Số phút từ lúc rời điểm xuất phát tới hết chặng cuối (gồm đoạn đi tới chặng đầu nếu có origin).</summary>
    public static int TotalMinutes(IReadOnlyList<ScheduleInput> orderedStops, TravelMode mode, ScheduleOrigin? origin)
    {
        if (orderedStops.Count == 0)
        {
            return 0;
        }

        var offsets = StartOffsets(orderedStops, mode, origin);
        return offsets[^1] + orderedStops[^1].DurationMinutes;
    }

    // Thứ tự đi (gần nhất) và số phút bắt đầu từng chặng theo thứ tự đó.
    private static (List<int> Order, List<int> Offsets) Arrange(
        IReadOnlyList<ScheduleInput> stops, TravelMode mode, ScheduleOrigin? origin)
    {
        var order = NearestNeighborOrder(stops);
        return (order, StartOffsets(order.Select(index => stops[index]).ToList(), mode, origin));
    }

    // Số phút từ lúc RỜI điểm xuất phát tới lúc bắt đầu từng chặng
    // = đi tới chặng đầu (nếu có origin) + tham quan các chặng trước + di chuyển giữa chúng.
    private static List<int> StartOffsets(IReadOnlyList<ScheduleInput> stops, TravelMode mode, ScheduleOrigin? origin)
    {
        var offsets = new List<int>(stops.Count);
        var offset = origin is null || stops.Count == 0
            ? 0
            : LegMinutes(origin.Latitude, origin.Longitude, stops[0], mode);

        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0)
            {
                offset += LegMinutes(stops[i - 1].Latitude, stops[i - 1].Longitude, stops[i], mode);
            }

            offsets.Add(offset);
            offset += stops[i].DurationMinutes;
        }

        return offsets;
    }

    private static int LegMinutes(double fromLatitude, double fromLongitude, ScheduleInput to, TravelMode mode) =>
        TravelTimeEstimator.EstimateMinutes(
            TravelTimeEstimator.RoadDistanceKm(fromLatitude, fromLongitude, to.Latitude, to.Longitude), mode);

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
