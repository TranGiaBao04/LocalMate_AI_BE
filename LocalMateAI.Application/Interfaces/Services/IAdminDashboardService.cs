using LocalMateAI.Application.DTOs.Dashboard;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminDashboardService
{
    Task<AdminDashboardResult<AdminDashboardSummaryResponse>> GetSummaryAsync(DashboardDateRangeQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminDashboardResult<AdminDashboardRevenueDailyResponse>> GetRevenueDailyAsync(DashboardDateRangeQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminDashboardResult<AdminDashboardTripsFinalizedDailyResponse>> GetTripsFinalizedDailyAsync(
        DashboardDateRangeQuery query, CancellationToken cancellationToken = default);

    Task<AdminDashboardResult<AdminDashboardTopStationsResponse>> GetTopStationsAsync(DashboardTopStationsQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminDashboardResult<AdminDashboardBreakEvenResponse>> GetBreakEvenAsync(DashboardBreakEvenQuery query,
        CancellationToken cancellationToken = default);
}
