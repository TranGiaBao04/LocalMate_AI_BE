using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripItemDeletionService(
    IUserRepository userRepository,
    IItineraryItemRepository itineraryItemRepository,
    IItineraryTimelineRecalculator timelineRecalculator) : ITripItemDeletionService
{
    public async Task<DeleteItineraryItemResult> DeleteAsync(
        Guid userId,
        Guid tripId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty || itemId == Guid.Empty)
        {
            return DeleteItineraryItemResult.InvalidIds();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return DeleteItineraryItemResult.MissingUser();
        }

        var outcome = await itineraryItemRepository.DeleteItemAndRecalculateTimelineAsync(
            tripId,
            itemId,
            userId,
            timelineRecalculator.Recalculate,
            cancellationToken);

        return outcome.Status switch
        {
            DeleteItineraryItemPersistenceStatus.Deleted =>
                DeleteItineraryItemResult.Succeeded(outcome.RemainingItems ?? []),
            DeleteItineraryItemPersistenceStatus.NotFound => DeleteItineraryItemResult.MissingItem(),
            DeleteItineraryItemPersistenceStatus.TripFinalized => DeleteItineraryItemResult.AlreadyFinalized(),
            DeleteItineraryItemPersistenceStatus.LastItem => DeleteItineraryItemResult.CannotDeleteLastItem(),
            _ => throw new InvalidOperationException("Unsupported delete itinerary item persistence status.")
        };
    }
}
