using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripItemReplacementService
{
    Task<ReplaceItineraryItemResult> ReplaceAsync(
        Guid userId,
        Guid tripId,
        Guid itemId,
        Guid newPlaceId,
        CancellationToken cancellationToken = default);
}
