using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripItemDeletionService
{
    Task<DeleteItineraryItemResult> DeleteAsync(
        Guid userId,
        Guid tripId,
        Guid itemId,
        CancellationToken cancellationToken = default);
}
