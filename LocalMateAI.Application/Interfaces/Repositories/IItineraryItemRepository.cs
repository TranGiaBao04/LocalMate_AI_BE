using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IItineraryItemRepository
{
    // Trả null nếu item không tồn tại, không thuộc tripId, hoặc trip không thuộc userId.
    Task<OwnedItemAlternativesReadModel?> GetOwnedItemForAlternativesAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default);

    // Trả null nếu không cập nhật được: item không còn thuộc user, trip đã Finalized,
    // hoặc newPlaceId vừa xuất hiện trong trip.
    Task<ReplacedItemReadModel?> ReplaceItemPlaceIfEligibleAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        Guid newPlaceId,
        decimal newEstimatedBudget,
        CancellationToken cancellationToken = default);
}
