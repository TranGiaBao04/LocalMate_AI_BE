using System.Globalization;
using LocalMateAI.Application.DTOs.Dashboard;

namespace LocalMateAI.Application.Services;

public static class DashboardDateRules
{
    public const string DateFormat = "yyyy-MM-dd";
    public const int DefaultRangeDays = 30;
    public const int MaxRangeDays = 366;

    public static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(VietnamTime.Now(timeProvider));

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Điền ngày mặc định. Trả null khi chuỗi sai định dạng (validator đã báo lỗi field đó).</summary>
    public static DashboardDateRange? Resolve(DashboardDateRangeQuery query, DateOnly today)
    {
        var to = today;
        if (!string.IsNullOrWhiteSpace(query.To) && !TryParseDate(query.To, out to))
        {
            return null;
        }

        var from = to.AddDays(-(DefaultRangeDays - 1));
        if (!string.IsNullOrWhiteSpace(query.From) && !TryParseDate(query.From, out from))
        {
            return null;
        }

        return new DashboardDateRange(from, to);
    }

    public const string MonthFormat = "yyyy-MM";
    public static readonly DateOnly MinMonth = new(2000, 1, 1);

    /// <summary>Ngày 1 của tháng. False khi sai định dạng yyyy-MM.</summary>
    public static bool TryParseMonth(string? value, out DateOnly firstDay)
    {
        firstDay = default;
        if (!DateTime.TryParseExact(value?.Trim(), MonthFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        firstDay = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
    }

    /// <summary>Ngày 1 của tháng được hỏi; bỏ trống ⇒ tháng hiện tại. Null khi sai định dạng.</summary>
    public static DateOnly? ResolveMonth(DashboardBreakEvenQuery query, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(query.Month))
        {
            return new DateOnly(today.Year, today.Month, 1);
        }

        return TryParseMonth(query.Month, out var firstDay) ? firstDay : null;
    }

    /// <summary>00:00 giờ VN của ngày đó, đổi sang UTC (VN cố định UTC+7).</summary>
    public static DateTime StartOfDayUtc(DateOnly vietnamDate) =>
        vietnamDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - VietnamTime.Offset;
}
