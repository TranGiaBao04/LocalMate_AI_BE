using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminFeedbackService
{
    Task<PagedResult<AdminReviewResponse>> GetReviewsAsync(
        AdminReviewQuery query,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AdminTripFeedbackResponse>> GetTripFeedbackAsync(
        AdminTripFeedbackQuery query,
        CancellationToken cancellationToken = default);

    // Query phải đã qua validator khoảng ngày của dashboard.
    Task<AdminFeedbackSummaryResponse> GetSummaryAsync(
        DashboardDateRangeQuery query,
        CancellationToken cancellationToken = default);
}
