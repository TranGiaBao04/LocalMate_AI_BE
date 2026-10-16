using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Services;

/// <summary>Một khoảng thời gian, tính bằng phút từ 00:00 của ngày đi (có thể âm hoặc ≥ 1440).</summary>
public sealed record MinuteRange(int Start, int End);

/// <summary>
/// Đổi giờ mở cửa theo thứ thành các khung mở cửa quanh ngày đi. Thuần.
/// Xét hôm trước, hôm đó, hôm sau: quán mở 18:00–02:00 thứ Bảy vẫn đang mở lúc 01:00 Chủ nhật.
/// Thứ chưa có dữ liệu ⇒ coi như mở 06:00–22:00 (ban đêm không dám xếp nơi chưa rõ giờ).
/// </summary>
public static class OpeningWindows
{
    public const int NightStartMinutes = 22 * 60;
    public const int NightEndMinutes = 6 * 60;

    /// <param name="assumeDaytimeForMissingDays">true (khi xếp lịch): thứ chưa nhập coi như mở 06:00–22:00.
    /// false (khi báo trạng thái): thứ chưa nhập không có khung nào.</param>
    public static IReadOnlyList<MinuteRange> Build(
        IReadOnlyList<PlaceOpeningHourDto> week,
        DateOnly plannedDate,
        bool assumeDaytimeForMissingDays = true)
    {
        var byDay = week.ToDictionary(day => day.DayOfWeek);
        var ranges = new List<MinuteRange>();

        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var dayStart = dayOffset * TripClock.MinutesPerDay;
            if (!byDay.TryGetValue(plannedDate.AddDays(dayOffset).DayOfWeek, out var row))
            {
                if (assumeDaytimeForMissingDays)
                {
                    ranges.Add(new MinuteRange(dayStart + NightEndMinutes, dayStart + NightStartMinutes));
                }

                continue;
            }

            if (row.IsClosed || row.OpenTime is not { } open || row.CloseTime is not { } close)
            {
                continue;
            }

            var start = dayStart + TripClock.MinuteOfDay(open);
            var end = dayStart + TripClock.MinuteOfDay(close);
            if (end <= start)
            {
                // Mở qua nửa đêm; 00:00–00:00 (mở cả ngày) cũng vào đây thành khung đủ 1440 phút
                // và được Merge nối liền với khung 00:00 của ngày kế tiếp.
                end += TripClock.MinutesPerDay;
            }

            ranges.Add(new MinuteRange(start, end));
        }

        return Merge(ranges);
    }

    /// <summary>Khung [start, end) nằm trọn trong một khung mở cửa (rời đúng giờ đóng vẫn được).</summary>
    public static bool Covers(IReadOnlyList<MinuteRange> windows, int start, int end) =>
        windows.Any(window => window.Start <= start && end <= window.End);

    /// <summary>
    /// Trạng thái mở cửa của một lần tham quan [start, end) (phút từ 00:00 ngày đi), chỉ dựa trên dữ liệu thật:
    /// nằm trọn trong khung đã nhập ⇒ Open; chạm vào thứ chưa nhập ⇒ Unknown; còn lại ⇒ Closed.
    /// </summary>
    public static PlaceOpeningStatus StatusFor(
        IReadOnlyList<PlaceOpeningHourDto> week, DateOnly plannedDate, int start, int end)
    {
        if (week.Count == 0)
        {
            return PlaceOpeningStatus.Unknown;
        }

        if (Covers(Build(week, plannedDate, assumeDaytimeForMissingDays: false), start, end))
        {
            return PlaceOpeningStatus.Open;
        }

        // Không nằm trọn trong khung đã biết: nếu lần tham quan chạm vào một thứ chưa nhập thì chưa kết luận được.
        var knownDays = week.Select(day => day.DayOfWeek).ToHashSet();
        var firstDay = (int)Math.Floor(start / (double)TripClock.MinutesPerDay);
        var lastDay = (int)Math.Floor((end - 1) / (double)TripClock.MinutesPerDay);
        var touchesUnknownDay = Enumerable.Range(firstDay, lastDay - firstDay + 1)
            .Any(offset => !knownDays.Contains(plannedDate.AddDays(offset).DayOfWeek));

        return touchesUnknownDay ? PlaceOpeningStatus.Unknown : PlaceOpeningStatus.Closed;
    }

    // Gộp khung liền nhau: 08:00–24:00 thứ Hai và 00:00–02:00 thứ Ba thành một khung.
    private static List<MinuteRange> Merge(List<MinuteRange> ranges)
    {
        var merged = new List<MinuteRange>();
        foreach (var range in ranges.OrderBy(range => range.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, range.End) };
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }
}
