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
                                && candidate.Trip.UserId == userId
                                && candidate.Trip.DeletedAt == null)
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
                           && item.Trip.DeletedAt == null
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

    public async Task<DeleteItineraryItemPersistenceResult> DeleteItemAndRecalculateTimelineAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        Func<TimelineRecalculationInput, IReadOnlyList<TimelineItemUpdate>> recalculateTimeline,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Khoá hàng trip để các thao tác xoá đồng thời trên cùng một trip chạy tuần tự.
        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "Trips" WHERE "Id" = {tripId} FOR UPDATE""",
            cancellationToken);

        var trip = await dbContext.Trips
            .AsNoTracking()
            .Where(candidate => candidate.Id == tripId && candidate.UserId == userId && candidate.DeletedAt == null)
            .Select(candidate => new { candidate.Status, candidate.TravelMode })
            .SingleOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.NotFound);
        }

        if (trip.Status == TripStatus.Finalized)
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.TripFinalized);
        }

        var items = await dbContext.ItineraryItems
            .AsNoTracking()
            .Where(item => item.TripId == tripId)
            .OrderBy(item => item.OrderIndex)
            .Select(item => new TimelineItemSnapshot(
                item.Id,
                item.OrderIndex,
                item.ScheduledTime,
                item.EstimatedDurationMinutes,
                item.Place.Location.Y,
                item.Place.Location.X))
            .ToListAsync(cancellationToken);

        if (items.All(item => item.ItemId != itemId))
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.NotFound);
        }

        if (items.Count == 1)
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.LastItem);
        }

        await dbContext.ItineraryItems
            .Where(item => item.Id == itemId && item.TripId == tripId)
            .ExecuteDeleteAsync(cancellationToken);

        // items[0] là chặng đầu trước khi xoá: nếu chính nó bị xoá, chặng đầu mới thừa hưởng giờ này.
        var updates = recalculateTimeline(new TimelineRecalculationInput(
            items.Where(item => item.ItemId != itemId).ToList(),
            items[0].ScheduledTime,
            trip.TravelMode));

        // Mọi item chỉ giảm OrderIndex nên cập nhật theo thứ tự tăng dần để không vi phạm unique (TripId, OrderIndex).
        var now = DateTime.UtcNow;
        foreach (var update in updates.OrderBy(update => update.OrderIndex))
        {
            await dbContext.ItineraryItems
                .Where(item => item.Id == update.ItemId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.OrderIndex, update.OrderIndex)
                    .SetProperty(item => item.ScheduledTime, update.ScheduledTime)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        }

        var remaining = await dbContext.ItineraryItems
            .AsNoTracking()
            .Where(item => item.TripId == tripId)
            .OrderBy(item => item.OrderIndex)
            .Select(item => new ItineraryTimelineItemResponse(
                item.Id,
                item.PlaceId,
                item.Place.Name,
                item.OrderIndex,
                item.ScheduledTime,
                item.EstimatedDurationMinutes,
                item.EstimatedBudget))
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.Deleted, remaining);
    }
}
