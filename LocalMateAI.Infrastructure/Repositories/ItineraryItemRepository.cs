using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class ItineraryItemRepository(AppDbContext dbContext) : IItineraryItemRepository
{
    public async Task<OwnedItemAlternativesReadModel?> GetOwnedItemForAlternativesAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var item = await dbContext.ItineraryItems
            .AsNoTracking()
            .Where(candidate => candidate.Id == itemId
                                && candidate.TripId == tripId
                                && candidate.Trip.UserId == userId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.TripId,
                candidate.Trip.Status,
                candidate.PlaceId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return null;
        }

        var tripPlaceIds = await dbContext.ItineraryItems
            .AsNoTracking()
            .Where(candidate => candidate.TripId == tripId)
            .Select(candidate => candidate.PlaceId)
            .ToListAsync(cancellationToken);

        return new OwnedItemAlternativesReadModel(
            item.Id,
            item.TripId,
            item.Status,
            item.PlaceId,
            tripPlaceIds);
    }
}
