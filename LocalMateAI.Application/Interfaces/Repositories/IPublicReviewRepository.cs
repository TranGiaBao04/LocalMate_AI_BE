using LocalMateAI.Application.DTOs.PublicReviews;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPublicReviewRepository
{
    /// <summary>
    /// Đánh giá có nhận xét, từ minRating sao trở lên, của địa điểm đang hiển thị;
    /// mỗi người dùng một đánh giá (cái mới nhất thoả điều kiện), mới nhất trước.
    /// QuickTags trả đủ như đã lưu, chưa lọc.
    /// </summary>
    Task<IReadOnlyList<PublicReviewItem>> GetFeaturedAsync(
        int minRating,
        int take,
        CancellationToken cancellationToken = default);
}
