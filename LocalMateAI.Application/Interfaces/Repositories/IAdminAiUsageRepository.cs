using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminAiUsageRepository
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AdminAiUsageSummaryData> GetSystemSummaryAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);
    Task<AdminAiUsageSummaryData> GetUserSummaryAsync(Guid userId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);
    Task<PagedResult<AdminAiCallReadModel>> GetUserCallsAsync(Guid userId, AdminAiCallsQuery query,
        CancellationToken cancellationToken = default);
}
