using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed record ScheduleInput(double Latitude, double Longitude, int DurationMinutes, decimal Cost = 0);

/// <param name="SourceIndex">Vị trí của chặng này trong danh sách đầu vào (vì Schedule có thể đổi thứ tự).</param>
public sealed record ScheduledSlot(int SourceIndex, TimeOnly ScheduledTime, int DurationMinutes);

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
    /// Chọn và xếp giờ: duyệt theo thứ hạng, thêm địa điểm nếu tổng thời lượng ≤ durationHours và tổng chi phí
    /// ≤ budgetMax, bỏ qua địa điểm không vừa rồi thử địa điểm sau. Luôn giữ chặng xếp hạng cao nhất.
    /// Các chặng được chọn rồi sắp theo gần nhất, chặng đầu là chặng xếp hạng cao nhất.
    /// </summary>
    public static IReadOnlyList<ScheduledSlot> Schedule(
        IReadOnlyList<ScheduleInput> rankedStops,
        TimeOnly startTime,
        int durationHours,
        TravelMode mode,
        decimal budgetMax)
    {
        if (rankedStops.Count == 0)
        {
            return [];
        }

        var limitMinutes = durationHours * 60;
        var selected = new List<int>();
        var spent = 0m;

        for (var candidate = 0; candidate < rankedStops.Count; candidate++)
        {
            if (selected.Count > 0)
            {
                if (spent + rankedStops[candidate].Cost > budgetMax)
                {
                    continue;
                }

                var trial = selected.Append(candidate).Select(index => rankedStops[index]).ToList();
                var (trialOrder, trialOffsets) = Arrange(trial, mode);
                if (trialOffsets[^1] + trial[trialOrder[^1]].DurationMinutes > limitMinutes)
                {
                    continue;
                }
            }

            selected.Add(candidate);
            spent += rankedStops[candidate].Cost;
        }

        var chosen = selected.Select(index => rankedStops[index]).ToList();
        var (order, offsets) = Arrange(chosen, mode);

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
        TravelMode mode)
    {
        var offsets = StartOffsets(orderedStops, mode);
        return orderedStops
            .Select((stop, index) => new ScheduledSlot(index, startTime.AddMinutes(offsets[index]), stop.DurationMinutes))
            .ToList();
    }

    // Thứ tự đi (gần nhất) và số phút bắt đầu từng chặng theo thứ tự đó.
    private static (List<int> Order, List<int> Offsets) Arrange(IReadOnlyList<ScheduleInput> stops, TravelMode mode)
    {
        var order = NearestNeighborOrder(stops);
        return (order, StartOffsets(order.Select(index => stops[index]).ToList(), mode));
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
