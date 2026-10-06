using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminFeedbackRepository
{
    Task<PagedResult<AdminReviewResponse>> GetReviewsAsync(
        AdminReviewQuery query,
        AdminFeedbackDateBounds bounds,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AdminTripFeedbackResponse>> GetTripFeedbackAsync(
        AdminTripFeedbackQuery query,
        AdminFeedbackDateBounds bounds,
        CancellationToken cancellationToken = default);

    // Chỉ tính đánh giá/phản hồi gửi trong [startUtc, endUtc), kể cả của địa điểm đã ẩn và chuyến đã xoá mềm.
    Task<AdminFeedbackSummaryData> GetSummaryDataAsync(
        DateTime startUtc,
        DateTime endUtc,
        int lowestRatedLimit,
        CancellationToken cancellationToken = default);
}
