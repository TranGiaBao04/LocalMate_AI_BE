using System.Globalization;
using LocalMateAI.Application.DTOs.Dashboard;

namespace LocalMateAI.Application.Services;

public static class BreakEvenCalculator
{
    public static AdminDashboardBreakEvenResponse Calculate(DateOnly firstDayOfMonth, DateOnly today, decimal target,
        PaidRevenueTotals totals, DateTime generatedAt)
    {
        var daysInMonth = DateTime.DaysInMonth(firstDayOfMonth.Year, firstDayOfMonth.Month);
        var isCurrentMonth = firstDayOfMonth.Year == today.Year && firstDayOfMonth.Month == today.Month;
        // Tháng hiện tại tính tới hôm nay (ngày 1 ⇒ 1, không chia cho 0); tháng cũ đã trôi hết.
        var daysElapsed = isCurrentMonth ? today.Day : daysInMonth;

        return new AdminDashboardBreakEvenResponse(
            firstDayOfMonth.ToString(DashboardDateRules.MonthFormat, CultureInfo.InvariantCulture),
            target,
            totals.Revenue,
            totals.PaidOrders,
            Math.Max(0m, target - totals.Revenue),
            Math.Round(totals.Revenue / target * 100m, 1, MidpointRounding.AwayFromZero),
            daysElapsed,
            daysInMonth,
            Math.Floor(totals.Revenue / daysElapsed * daysInMonth),
            totals.Revenue >= target,
            AdminDashboardService.Currency,
            generatedAt);
    }
}
