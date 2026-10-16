using LocalMateAI.Application.DTOs.Places;

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

    public static IReadOnlyList<MinuteRange> Build(IReadOnlyList<PlaceOpeningHourDto> week, DateOnly plannedDate)
    {
        var byDay = week.ToDictionary(day => day.DayOfWeek);
        var ranges = new List<MinuteRange>();

        for (var dayOffset = -1; dayOffset <= 1; dayOffset++)
        {
            var dayStart = dayOffset * TripClock.MinutesPerDay;
            if (!byDay.TryGetValue(plannedDate.AddDays(dayOffset).DayOfWeek, out var row))
            {
                ranges.Add(new MinuteRange(dayStart + NightEndMinutes, dayStart + NightStartMinutes));
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
                end += TripClock.MinutesPerDay; // mở qua nửa đêm
            }

            ranges.Add(new MinuteRange(start, end));
        }

        return Merge(ranges);
    }

    /// <summary>Khung [start, end) nằm trọn trong một khung mở cửa (rời đúng giờ đóng vẫn được).</summary>
    public static bool Covers(IReadOnlyList<MinuteRange> windows, int start, int end) =>
        windows.Any(window => window.Start <= start && end <= window.End);

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
