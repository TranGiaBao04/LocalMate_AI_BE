using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Tính tổng chi phí và thời lượng của một chuyến đi từ danh sách chặng (BE-44).
/// Thời gian di chuyển không lưu riêng mà suy ra từ khoảng trống giữa giờ kết thúc chặng trước
/// và giờ bắt đầu chặng sau, nên không cần đổi schema.
/// </summary>
public static class TripTotalsCalculator
{
    /// <param name="orderedStops">Các chặng đã xếp theo OrderIndex tăng dần.</param>
    public static TripTotals Calculate(IReadOnlyList<TripStopSnapshot> orderedStops)
    {
        if (orderedStops.Count == 0)
        {
            return new TripTotals(0, 0, 0, 0, null);
        }

        var visitMinutes = 0;
        var travelMinutes = 0;
        decimal budget = 0;

        for (var index = 0; index < orderedStops.Count; index++)
        {
            var stop = orderedStops[index];
            visitMinutes += stop.EstimatedDurationMinutes;
            budget += stop.EstimatedBudget;

            if (index == 0)
            {
                continue;
            }

            var previous = orderedStops[index - 1];
            var previousEnd = previous.ScheduledTime.ToTimeSpan()
                              + TimeSpan.FromMinutes(previous.EstimatedDurationMinutes);
            var gap = (int)(stop.ScheduledTime.ToTimeSpan() - previousEnd).TotalMinutes;
            travelMinutes += Math.Max(0, gap); // giờ chồng lấn thì coi như không di chuyển
        }

        var last = orderedStops[^1];
        return new TripTotals(
            budget,
            visitMinutes,
            travelMinutes,
            visitMinutes + travelMinutes,
            last.ScheduledTime.AddMinutes(last.EstimatedDurationMinutes));
    }
}
