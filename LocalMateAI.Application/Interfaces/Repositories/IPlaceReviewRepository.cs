using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPlaceReviewRepository
{
    // Trả null nếu chặng không tồn tại, không thuộc userId, hoặc trip đã bị xoá mềm.
    Task<OwnedItemForReviewReadModel?> GetOwnedItemAsync(
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PlaceReview?> GetAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    // Trả false nếu vi phạm unique (userId, itemId) do hai request đua nhau.
    Task<bool> TryAddAsync(
        PlaceReview review,
        CancellationToken cancellationToken = default);

    // Trả số dòng đã xoá: 0 khi user chưa đánh giá chặng này (hoặc request khác vừa xoá trước).
    Task<int> DeleteAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    // Địa điểm đang hiển thị cho user: Active và chưa xoá mềm.
    Task<bool> IsPlaceVisibleAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    // Tính mọi đánh giá của địa điểm, kể cả từ trip đã xoá mềm.
    Task<PlaceReviewSummary> GetSummaryAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<PublicPlaceReviewResponse>> GetPagedByPlaceAsync(
        Guid placeId,
        PlaceReviewQuery query,
        CancellationToken cancellationToken = default);
}
