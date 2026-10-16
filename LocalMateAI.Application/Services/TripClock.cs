using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Đổi giờ đã lưu của các chặng (TimeOnly, quay vòng qua 00:00) thành số phút tính từ 00:00 của ngày đi,
/// để chặng sau nửa đêm có số phút ≥ 1440. Thuần. Không lưu cột "ngày": suy ra từ thứ tự chặng.
/// </summary>
public static class TripClock
{
    public const int MinutesPerDay = 24 * 60;

    // Giờ lùi hơn mốc này so với mốc trước thì coi là đã sang ngày mới; lùi ít hơn là dữ liệu chồng giờ (giữ ngày).
    // Không đoạn đi nào dài tới 12 tiếng nên hai trường hợp không lẫn nhau.
    public const int DayRolloverThresholdMinutes = 12 * 60;

    public static int MinuteOfDay(TimeOnly time) => time.Hour * 60 + time.Minute;

    /// <summary>Chặng đầu có giờ sớm hơn giờ rời điểm xuất phát quá 12 tiếng thì thuộc ngày hôm sau.</summary>
    public static bool StartsNextDay(TimeOnly leaveAt, TimeOnly firstStop) =>
        MinuteOfDay(leaveAt) - MinuteOfDay(firstStop) > DayRolloverThresholdMinutes;

    /// <summary>Ngày của chặng đầu: ngày đi, hoặc ngày hôm sau nếu chặng đầu đã qua nửa đêm.</summary>
    public static DateOnly DateOfFirstStop(DateTime plannedStartAt, TimeOnly firstStop)
    {
        var date = DateOnly.FromDateTime(plannedStartAt);
        return StartsNextDay(TimeOnly.FromDateTime(plannedStartAt), firstStop) ? date.AddDays(1) : date;
    }

    /// <summary>Số phút bắt đầu của từng chặng, tính từ 00:00 ngày đi.</summary>
    /// <param name="leaveAt">Giờ rời điểm xuất phát; null với trip cũ chưa có PlannedStartAt.</param>
    /// <param name="orderedStops">Các chặng theo OrderIndex tăng dần.</param>
    public static IReadOnlyList<int> StartMinutes(TimeOnly? leaveAt, IReadOnlyList<TripStopSnapshot> orderedStops)
    {
        var starts = new List<int>(orderedStops.Count);
        int? previousMark = leaveAt is { } leave ? MinuteOfDay(leave) : null;
        var dayOffset = 0;

        foreach (var stop in orderedStops)
        {
            var start = MinuteOfDay(stop.ScheduledTime) + dayOffset;
            if (previousMark is { } mark && mark - start > DayRolloverThresholdMinutes)
            {
                dayOffset += MinutesPerDay;
                start += MinutesPerDay;
            }

            starts.Add(start);
            previousMark = start + stop.EstimatedDurationMinutes;
        }

        return starts;
    }
}
