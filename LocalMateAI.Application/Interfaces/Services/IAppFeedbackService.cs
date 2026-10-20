using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAppFeedbackService
{
    Task<CreateAppFeedbackResult> SubmitFeedbackAsync(
        Guid userId,
        CreateAppFeedbackRequest request,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AdminAppFeedbackItemResponse>> GetAdminListAsync(
        AdminAppFeedbackQuery query,
        CancellationToken cancellationToken = default);
}
