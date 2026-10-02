using LocalMateAI.Application.Services;

namespace LocalMateAI.Application.DTOs.Dashboard;

/// <summary>Khoảng ngày theo giờ VN (yyyy-MM-dd), tính cả 2 đầu. Bỏ trống ⇒ 30 ngày tới hôm nay.</summary>
public record DashboardDateRangeQuery
{
    public string? From { get; init; }
    public string? To { get; init; }
}

/// <summary>Khoảng ngày VN đã chuẩn hoá. Truy vấn dùng nửa mở [StartUtc, EndUtc).</summary>
public sealed record DashboardDateRange(DateOnly From, DateOnly To)
{
    public DateTime StartUtc => DashboardDateRules.StartOfDayUtc(From);
    public DateTime EndUtc => DashboardDateRules.StartOfDayUtc(To.AddDays(1));
    public int DayCount => To.DayNumber - From.DayNumber + 1;
}

public sealed record DashboardTopStationsQuery : DashboardDateRangeQuery
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 14;

    public int Limit { get; init; } = DefaultLimit;
}

public sealed record StationTripCountRow(int StationOrder, string StationName, long TripCount);

public sealed record TopStationResponse(int Order, string Name, long TripCount, decimal SharePercent);

public sealed record AdminDashboardTopStationsResponse(
    DateOnly From,
    DateOnly To,
    long TotalTrips,
    IReadOnlyList<TopStationResponse> Stations,
    DateTime GeneratedAt);

/// <summary>Tháng theo giờ VN (yyyy-MM). Bỏ trống ⇒ tháng hiện tại.</summary>
public sealed record DashboardBreakEvenQuery
{
    public string? Month { get; init; }
}

public sealed record PaidRevenueTotals(long PaidOrders, decimal Revenue);

public sealed record DailyRevenueRow(DateOnly Date, long PaidOrders, decimal Revenue);

public sealed record DailyRevenueResponse(DateOnly Date, decimal Revenue, long PaidOrders);

public sealed record AdminDashboardRevenueDailyResponse(
    DateOnly From,
    DateOnly To,
    string Currency,
    decimal TotalRevenue,
    IReadOnlyList<DailyRevenueResponse> Days,
    DateTime GeneratedAt);

public sealed record AdminDashboardBreakEvenResponse(
    string Month,
    decimal Target,
    decimal Revenue,
    long PaidOrders,
    decimal Remaining,
    decimal ProgressPercent,
    int DaysElapsed,
    int DaysInMonth,
    decimal Projected,
    bool IsAchieved,
    string Currency,
    DateTime GeneratedAt);

public sealed record DashboardSummaryCounts(long NewUsers, long TripsCreated, long PaidOrders, decimal Revenue);

public sealed record AdminDashboardSummaryResponse(
    DateOnly From,
    DateOnly To,
    long NewUsers,
    long TripsCreated,
    long PaidOrders,
    decimal Revenue,
    string Currency,
    DateTime GeneratedAt);

public enum AdminDashboardResultStatus
{
    Success,
    InvalidQuery
}

public sealed record AdminDashboardResult<T>(
    AdminDashboardResultStatus Status,
    T? Response = default,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
