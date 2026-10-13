using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminAiUsageService
{
    Task<AdminAiUsageResult<AdminAiUsageSystemResponse>> GetSummaryAsync(DashboardDateRangeQuery query,
        CancellationToken cancellationToken = default);
    Task<AdminAiUsageResult<AdminAiUsageUserResponse>> GetUserSummaryAsync(Guid userId, DashboardDateRangeQuery query,
        CancellationToken cancellationToken = default);
    Task<AdminAiUsageResult<PagedResult<AdminAiCallReadModel>>> GetUserCallsAsync(Guid userId, AdminAiCallsRequest query,
        CancellationToken cancellationToken = default);
}
