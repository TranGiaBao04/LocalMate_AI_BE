using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
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

    public async Task<ReplacedItemReadModel?> ReplaceItemPlaceIfEligibleAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        Guid newPlaceId,
        decimal newEstimatedBudget,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Một lệnh có điều kiện để tránh race: trip vừa Finalized hoặc địa điểm mới vừa được thêm vào trip.
        var rowsChanged = await dbContext.ItineraryItems
            .Where(item => item.Id == itemId
                           && item.TripId == tripId
                           && item.Trip.UserId == userId
                           && item.Trip.Status == TripStatus.Draft
                           && !dbContext.ItineraryItems.Any(other =>
                               other.TripId == tripId && other.PlaceId == newPlaceId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.PlaceId, newPlaceId)
                .SetProperty(item => item.EstimatedBudget, newEstimatedBudget)
                .SetProperty(item => item.Reasoning, (string?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (rowsChanged != 1)
        {
            return null;
        }

        return await dbContext.ItineraryItems
            .AsNoTracking()
            .Where(item => item.Id == itemId)
            .Select(item => new ReplacedItemReadModel(
                item.Id,
                item.TripId,
                item.OrderIndex,
                item.ScheduledTime,
                item.EstimatedDurationMinutes,
                item.EstimatedBudget))
            .SingleAsync(cancellationToken);
    }
}
