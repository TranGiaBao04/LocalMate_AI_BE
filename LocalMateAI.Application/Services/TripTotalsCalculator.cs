using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Tính tổng chi phí và thời lượng của một chuyến đi từ danh sách chặng (BE-44).
/// Thời gian di chuyển không lưu riêng mà suy ra từ khoảng trống giữa giờ kết thúc chặng trước
/// và giờ bắt đầu chặng sau, nên không cần đổi schema.
/// </summary>
public static class TripTotalsCalculator
{
    /// <summary>
    /// Thời gian di chuyển tới từng chặng = khoảng trống giữa lúc chặng trước kết thúc và chặng này bắt đầu
    /// (tính qua nửa đêm đúng nhờ TripClock). Phần tử đầu luôn 0; giờ chồng lấn thì coi như không di chuyển.
    /// </summary>
    public static IReadOnlyList<int> TravelGaps(IReadOnlyList<TripStopSnapshot> orderedStops)
    {
        var starts = TripClock.StartMinutes(leaveAt: null, orderedStops);
        return starts
            .Select((start, index) => index == 0
                ? 0
                : Math.Max(0, start - starts[index - 1] - orderedStops[index - 1].EstimatedDurationMinutes))
            .ToList();
    }

    /// <param name="orderedStops">Các chặng đã xếp theo OrderIndex tăng dần.</param>
    public static TripTotals Calculate(IReadOnlyList<TripStopSnapshot> orderedStops)
    {
        if (orderedStops.Count == 0)
        {
            return new TripTotals(0, 0, 0, 0, null);
        }

        var visitMinutes = orderedStops.Sum(stop => stop.EstimatedDurationMinutes);
        var travelMinutes = TravelGaps(orderedStops).Sum();
        var budget = orderedStops.Sum(stop => stop.EstimatedBudget);
        var last = orderedStops[^1];

        return new TripTotals(
            budget,
            visitMinutes,
            travelMinutes,
            visitMinutes + travelMinutes,
            last.ScheduledTime.AddMinutes(last.EstimatedDurationMinutes)); // giờ trong ngày; "hôm sau" thêm ở Phần 6
    }
}
