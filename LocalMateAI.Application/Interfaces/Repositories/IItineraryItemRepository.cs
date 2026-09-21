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
}
