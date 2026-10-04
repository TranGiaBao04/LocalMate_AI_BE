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
                candidate.PlaceId,
                candidate.Trip.PlannedStartAt,
                candidate.Trip.DurationHours
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
            tripPlaceIds,
            item.PlannedStartAt,
            item.DurationHours);
    }

    public async Task<ReplaceItemPersistenceResult> ReplaceItemPlaceAndRecalculateTimelineAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        Guid newPlaceId,
        decimal newEstimatedBudget,
        Func<TimelineRecalculationInput, IReadOnlyList<TimelineItemUpdate>?> recalculateTimeline,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Khoá hàng trip để thay/xoá chặng đồng thời trên cùng một trip chạy tuần tự (giờ không bị tính trên dữ liệu cũ).
        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "Trips" WHERE "Id" = {tripId} FOR UPDATE""",
            cancellationToken);

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
            return new ReplaceItemPersistenceResult(ReplaceItemPersistenceStatus.NotEligible);
        }

        var trip = await dbContext.Trips
            .AsNoTracking()
            .Where(candidate => candidate.Id == tripId)
            .Select(candidate => new { candidate.TravelMode, candidate.PlannedStartAt })
            .SingleAsync(cancellationToken);

        // Các chặng sau lệnh thay: chặng được thay đã mang toạ độ địa điểm mới. Chặng đầu giữ giờ của nó làm mốc.
        var items = await WithStationsAsync(
            await LoadTimelineSnapshotsAsync(tripId, cancellationToken), tripId, trip.TravelMode, cancellationToken);
        var updates = recalculateTimeline(new TimelineRecalculationInput(
            items,
            items[0].ScheduledTime,
            trip.TravelMode,
            trip.PlannedStartAt is { } plannedStartAt ? DateOnly.FromDateTime(plannedStartAt) : null));
        if (updates is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ReplaceItemPersistenceResult(ReplaceItemPersistenceStatus.CrossesMidnight);
        }

        await ApplyTimelineUpdatesAsync(updates, now, cancellationToken);
        var timeline = await LoadTimelineAsync(tripId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var replaced = timeline.Single(item => item.ItemId == itemId);
        return new ReplaceItemPersistenceResult(
            ReplaceItemPersistenceStatus.Replaced,
            new ReplacedItemReadModel(
                replaced.ItemId,
                tripId,
                replaced.OrderIndex,
                replaced.ScheduledTime,
                replaced.EstimatedDurationMinutes,
                replaced.EstimatedBudget),
            timeline);
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
            .Select(candidate => new { candidate.Status, candidate.TravelMode, candidate.PlannedStartAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (trip is null)
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.NotFound);
        }

        if (trip.Status == TripStatus.Finalized)
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.TripFinalized);
        }

        var items = await LoadTimelineSnapshotsAsync(tripId, cancellationToken);

        if (items.All(item => item.ItemId != itemId))
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.NotFound);
        }

        if (items.Count == 1)
        {
            return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.LastItem);
        }

        items = await WithStationsAsync(items, tripId, trip.TravelMode, cancellationToken);

        await dbContext.ItineraryItems
            .Where(item => item.Id == itemId && item.TripId == tripId)
            .ExecuteDeleteAsync(cancellationToken);

        // items[0] là chặng đầu trước khi xoá: nếu chính nó bị xoá, chặng đầu mới thừa hưởng giờ này.
        var updates = recalculateTimeline(new TimelineRecalculationInput(
            items.Where(item => item.ItemId != itemId).ToList(),
            items[0].ScheduledTime,
            trip.TravelMode,
            trip.PlannedStartAt is { } plannedStartAt ? DateOnly.FromDateTime(plannedStartAt) : null));

        await ApplyTimelineUpdatesAsync(updates, DateTime.UtcNow, cancellationToken);
        var remaining = await LoadTimelineAsync(tripId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new DeleteItineraryItemPersistenceResult(DeleteItineraryItemPersistenceStatus.Deleted, remaining);
    }

    private async Task<List<TimelineItemSnapshot>> LoadTimelineSnapshotsAsync(
        Guid tripId, CancellationToken cancellationToken) =>
        await dbContext.ItineraryItems
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

    // Trip Metro: tính lại giờ cần biết mỗi chặng thuộc ga nào và cách ga bao xa (để tính đi bộ ra ga, đi tàu).
    private async Task<List<TimelineItemSnapshot>> WithStationsAsync(
        List<TimelineItemSnapshot> items, Guid tripId, TravelMode travelMode, CancellationToken cancellationToken)
    {
        if (travelMode != TravelMode.Metro)
        {
            return items;
        }

        var stationsByItemId = (await TripStationQueries.GetItemStationsAsync(dbContext, tripId, cancellationToken))
            .ToDictionary(row => row.ItemId);
        return items
            .Select(item => stationsByItemId.TryGetValue(item.ItemId, out var station)
                ? item with { StationOrder = station.StationOrder, DistanceFromStationMeters = station.DistanceMeters }
                : item)
            .ToList();
    }

    // Cập nhật theo OrderIndex tăng dần: khi xoá chặng mọi item chỉ giảm OrderIndex, làm theo thứ tự này thì không
    // vi phạm unique (TripId, OrderIndex).
    private async Task ApplyTimelineUpdatesAsync(
        IReadOnlyList<TimelineItemUpdate> updates, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var update in updates.OrderBy(update => update.OrderIndex))
        {
            await dbContext.ItineraryItems
                .Where(item => item.Id == update.ItemId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.OrderIndex, update.OrderIndex)
                    .SetProperty(item => item.ScheduledTime, update.ScheduledTime)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        }
    }

    private async Task<List<ItineraryTimelineItemResponse>> LoadTimelineAsync(
        Guid tripId, CancellationToken cancellationToken) =>
        await dbContext.ItineraryItems
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
}
