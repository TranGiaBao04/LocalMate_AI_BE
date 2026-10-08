using LocalMateAI.Application.DTOs.Dashboard;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminDashboardRepository
{
    Task<DashboardSummaryCounts> GetSummaryAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default);

    Task<PaidRevenueTotals> GetPaidRevenueAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Chỉ trả những ngày (giờ VN) có đơn Paid, tăng dần.</summary>
    Task<IReadOnlyList<DailyRevenueRow>> GetDailyRevenueAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Số trip đã chốt theo ngày chốt (giờ VN), tăng dần. Chỉ trả những ngày có ≥ 1 trip.</summary>
    Task<IReadOnlyList<DailyTripsFinalizedResponse>> GetDailyFinalizedTripsAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Số trip tạo trong khoảng, gom theo ga gần điểm xuất phát nhất. Chỉ trả ga có ≥ 1 trip.</summary>
    Task<IReadOnlyList<StationTripCountRow>> GetTripCountsByStationAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default);
}
