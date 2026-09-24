using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed record ScheduleInput(double Latitude, double Longitude, int DurationMinutes);

/// <param name="SourceIndex">Vị trí của chặng này trong danh sách đầu vào (vì Schedule có thể đổi thứ tự).</param>
public sealed record ScheduledSlot(int SourceIndex, TimeOnly ScheduledTime, int DurationMinutes);

/// <summary>
/// Xếp giờ cho các chặng (BE-42). Thuần. Giờ là TimeOnly nên chuyến qua nửa đêm sẽ quay về 00:00 (chưa hỗ trợ ngày).
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

    /// <summary>
    /// Dùng khi tạo mới: giữ chặng đầu (điểm cao nhất), sắp các chặng sau theo gần nhất,
    /// rồi cắt bớt các chặng cuối nếu vượt durationHours (luôn giữ ít nhất 1 chặng).
    /// </summary>
    public static IReadOnlyList<ScheduledSlot> Schedule(
        IReadOnlyList<ScheduleInput> rankedStops,
        TimeOnly startTime,
        int durationHours,
        TravelMode mode)
    {
        if (rankedStops.Count == 0)
        {
            return [];
        }

        var order = NearestNeighborOrder(rankedStops);
        var ordered = order.Select(index => rankedStops[index]).ToList();
        var offsets = StartOffsets(ordered, mode);
        var limitMinutes = durationHours * 60;

        var slots = new List<ScheduledSlot>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i > 0 && offsets[i] + ordered[i].DurationMinutes > limitMinutes)
            {
                break; // offsets tăng dần nên các chặng sau cũng vượt
            }

            slots.Add(new ScheduledSlot(order[i], startTime.AddMinutes(offsets[i]), ordered[i].DurationMinutes));
        }

        return slots;
    }

    /// <summary>Dùng khi sửa lịch (xoá chặng...): giữ nguyên thứ tự và thời lượng, chỉ tính lại giờ.</summary>
    public static IReadOnlyList<ScheduledSlot> Reschedule(
        IReadOnlyList<ScheduleInput> orderedStops,
        TimeOnly startTime,
        TravelMode mode)
    {
        var offsets = StartOffsets(orderedStops, mode);
        return orderedStops
            .Select((stop, index) => new ScheduledSlot(index, startTime.AddMinutes(offsets[index]), stop.DurationMinutes))
            .ToList();
    }

    // Số phút từ lúc bắt đầu chuyến tới lúc bắt đầu từng chặng = tham quan các chặng trước + di chuyển giữa chúng.
    private static List<int> StartOffsets(IReadOnlyList<ScheduleInput> stops, TravelMode mode)
    {
        var offsets = new List<int>(stops.Count);
        var offset = 0;
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0)
            {
                var previous = stops[i - 1];
                var roadKm = TravelTimeEstimator.RoadDistanceKm(
                    previous.Latitude, previous.Longitude, stops[i].Latitude, stops[i].Longitude);
                offset += TravelTimeEstimator.EstimateMinutes(roadKm, mode);
            }

            offsets.Add(offset);
            offset += stops[i].DurationMinutes;
        }

        return offsets;
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
