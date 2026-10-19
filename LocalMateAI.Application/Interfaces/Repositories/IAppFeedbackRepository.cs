using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAppFeedbackRepository
{
    Task<int> CountUserSubmissionsSinceUtcAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    Task<AppFeedback> CreateAsync(AppFeedback feedback, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminAppFeedbackItemResponse>> GetAdminListAsync(
        AdminAppFeedbackQuery query,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default);
}
