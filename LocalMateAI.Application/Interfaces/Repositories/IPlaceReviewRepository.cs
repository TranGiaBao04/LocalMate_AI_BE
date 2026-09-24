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
}
