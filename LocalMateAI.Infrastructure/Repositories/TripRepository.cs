using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class TripRepository(AppDbContext dbContext) : ITripRepository
{
    public Task<int> CountNormalFinalizedByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Trips.AsNoTracking().CountAsync(t => t.UserId == userId && t.Status == TripStatus.Finalized
            && t.DeletedAt == null && !dbContext.SingleItineraryEntitlements.Any(e =>
                e.UserId == userId && e.ConsumedTripId == t.Id && e.ConsumedAt != null), cancellationToken);
    public Task<Trip?> LockOwnedForFinalizeAsync(Guid tripId, Guid userId, CancellationToken ct = default) =>
        dbContext.Trips.FromSqlInterpolated(
            $"""SELECT * FROM "Trips" WHERE "Id" = {tripId} AND "UserId" = {userId} AND "DeletedAt" IS NULL FOR UPDATE""")
            .AsNoTracking().SingleOrDefaultAsync(ct);
    public Task<int> CountFinalizedByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Trips
            .AsNoTracking()
            .CountAsync(trip => trip.UserId == userId
                                && trip.Status == TripStatus.Finalized
                                && trip.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var trips = await dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.UserId == userId && trip.DeletedAt == null)
            .OrderByDescending(trip => trip.UpdatedAt)
            .ThenByDescending(trip => trip.CreatedAt)
            .ThenBy(trip => trip.Id)
            .Select(trip => new
            {
                trip.Id,
                trip.Status,
                trip.StartLatitude,
                trip.StartLongitude,
                trip.DurationHours,
                trip.BudgetMin,
                trip.BudgetMax,
                ItemCount = trip.Items.Count,
                EstimatedBudget = trip.Items.Sum(item => item.EstimatedBudget),
                trip.CreatedAt,
                trip.UpdatedAt,
                trip.FinalizedAt,
                trip.PlannedStartAt
            })
            .ToListAsync(cancellationToken);

        // Ga gần nhất (đường chim bay, geography — cùng cách tính với MetroStationRepository.FindNearestAsync).
        var stationsByTripId = (await dbContext.Database.SqlQuery<TripStationRow>(
                $"""
                 SELECT t."Id" AS "TripId", s."Name" AS "StationName"
                 FROM "Trips" t
                 CROSS JOIN LATERAL (
                     SELECT ms."Name"
                     FROM "MetroStations" ms
                     ORDER BY ST_Distance(
                         ST_SetSRID(ST_MakePoint(t."StartLongitude", t."StartLatitude"), 4326)::geography,
                         ms."Location"::geography)
                     LIMIT 1
                 ) s
                 WHERE t."UserId" = {userId} AND t."DeletedAt" IS NULL
                 """)
            .ToListAsync(cancellationToken))
            .ToDictionary(row => row.TripId, row => row.StationName);

        return trips
            .Select(trip => new MyTripReadModel(
                trip.Id,
                trip.Status,
                trip.StartLatitude,
                trip.StartLongitude,
                trip.DurationHours,
                trip.BudgetMin,
                trip.BudgetMax,
                trip.ItemCount,
                trip.CreatedAt,
                trip.UpdatedAt,
                stationsByTripId.GetValueOrDefault(trip.Id),
                trip.EstimatedBudget,
                trip.FinalizedAt,
                trip.PlannedStartAt))
            .ToList();
    }

    public Task<Trip?> GetByIdAsync(
        Guid tripId,
        CancellationToken cancellationToken = default) =>
        dbContext.Trips
            .AsNoTracking()
            .SingleOrDefaultAsync(
                trip => trip.Id == tripId && trip.DeletedAt == null,
                cancellationToken);

    public async Task<bool> AttachUserIfUnownedAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var updatedAt = DateTime.UtcNow;
        var rowsChanged = await dbContext.Trips
            .Where(trip => trip.Id == tripId && trip.UserId == null && trip.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(trip => trip.UserId, userId)
                .SetProperty(trip => trip.UpdatedAt, updatedAt), cancellationToken);

        return rowsChanged == 1;
    }

    public async Task<bool> FinalizeTripAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var updatedAt = DateTime.UtcNow;
        var rowsChanged = await dbContext.Trips
            .Where(trip => trip.Id == tripId
                           && trip.UserId == userId
                           && trip.DeletedAt == null
                           && trip.Status == TripStatus.Draft)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(trip => trip.Status, TripStatus.Finalized)
                .SetProperty(trip => trip.FinalizedAt, (DateTime?)updatedAt)
                .SetProperty(trip => trip.UpdatedAt, updatedAt), cancellationToken);

        return rowsChanged == 1;
    }

    public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.ItineraryItems
            .AsNoTracking()
            .Where(item => item.Id == itemId
                           && item.Trip.UserId == userId
                           && item.Trip.DeletedAt == null)
            .Select(item => new OwnedItineraryItemVisitReadModel(
                item.Id,
                item.TripId,
                item.Trip.Status,
                item.IsVisited,
                item.VisitedAt,
                item.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> MarkItineraryItemVisitedIfEligibleAsync(
        Guid itemId,
        Guid userId,
        DateTimeOffset visitedAt,
        CancellationToken cancellationToken = default)
    {
        var rowsChanged = await dbContext.ItineraryItems
            .Where(item => item.Id == itemId
                           && item.Trip.UserId == userId
                           && item.Trip.DeletedAt == null
                           && item.Trip.Status == TripStatus.Finalized
                           && !item.IsVisited)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsVisited, true)
                .SetProperty(item => item.VisitedAt, visitedAt)
                .SetProperty(item => item.UpdatedAt, visitedAt.UtcDateTime), cancellationToken);

        return rowsChanged == 1;
    }

    public async Task<Trip?> ForkTripAsync(
        Guid sourceTripId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Items)
            .Include(trip => trip.Tags)
            .SingleOrDefaultAsync(
                trip => trip.Id == sourceTripId && trip.UserId == userId && trip.DeletedAt == null,
                cancellationToken);
        if (source is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var copyId = Guid.NewGuid();
        var copy = new Trip
        {
            Id = copyId,
            UserId = userId,
            StartLatitude = source.StartLatitude,
            StartLongitude = source.StartLongitude,
            StartStationId = source.StartStationId,
            DestinationStationId = source.DestinationStationId,
            Note = source.Note,
            NoteApplied = source.NoteApplied,
            DurationHours = source.DurationHours,
            BudgetMin = source.BudgetMin,
            BudgetMax = source.BudgetMax,
            TravelMode = source.TravelMode,
            Status = TripStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            Items = source.Items.Select(item => new ItineraryItem
            {
                Id = Guid.NewGuid(),
                TripId = copyId,
                PlaceId = item.PlaceId,
                OrderIndex = item.OrderIndex,
                ScheduledTime = item.ScheduledTime,
                EstimatedDurationMinutes = item.EstimatedDurationMinutes,
                EstimatedBudget = item.EstimatedBudget,
                Reasoning = item.Reasoning,
                IsVisited = false, // bản nháp mới chưa ghé đâu
                VisitedAt = null,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList(),
            Tags = source.Tags.Select(tag => new TripTag
            {
                TripId = copyId,
                TagId = tag.TagId
            }).ToList()
        };

        dbContext.Trips.Add(copy);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Nguồn bị xóa ngay giữa lúc fork (race hiếm) → coi như không tồn tại
            return null;
        }

        return copy;
    }

    public async Task<TripDetailReadModel?> GetOwnedDetailAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var trip = await dbContext.Trips
            .AsNoTracking()
            .Where(candidate => candidate.Id == tripId
                                && candidate.UserId == userId
                                && candidate.DeletedAt == null)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Status,
                candidate.TravelMode,
                candidate.StartLatitude,
                candidate.StartLongitude,
                candidate.DurationHours,
                candidate.BudgetMin,
                candidate.BudgetMax,
                candidate.CreatedAt,
                candidate.UpdatedAt,
                candidate.FinalizedAt,
                candidate.PlannedStartAt,
                candidate.StartStationId,
                candidate.DestinationStationId,
                candidate.Note,
                candidate.NoteApplied
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return null;
        }

        var tagIds = await dbContext.Trips
            .AsNoTracking()
            .Where(candidate => candidate.Id == tripId)
            .SelectMany(candidate => candidate.Tags)
            .Select(tag => tag.TagId)
            .ToListAsync(cancellationToken);

        var items = await dbContext.ItineraryItems
            .AsNoTracking()
            .Where(item => item.TripId == tripId)
            .OrderBy(item => item.OrderIndex)
            .Select(item => new
            {
                item.Id,
                item.PlaceId,
                PlaceName = item.Place.Name,
                item.Place.Address,
                item.Place.Category,
                item.Place.ImageUrl,
                Latitude = item.Place.Location.Y,
                Longitude = item.Place.Location.X,
                item.OrderIndex,
                item.ScheduledTime,
                item.EstimatedDurationMinutes,
                item.EstimatedBudget,
                item.Reasoning,
                item.IsVisited,
                item.VisitedAt
            })
            .ToListAsync(cancellationToken);

        // Ga lên: ga gần điểm xuất phát nhất (với trip xuất phát từ ga thì chính là ga đó, khoảng cách 0).
        var tripStation = await TripStationQueries.GetNearestStationAsync(
            dbContext, trip.StartLatitude, trip.StartLongitude, cancellationToken);

        var stationsByItemId = (await TripStationQueries.GetItemStationsAsync(dbContext, tripId, cancellationToken))
            .ToDictionary(row => row.ItemId);

        // Ga người dùng đã chọn lúc tạo lịch (xuất phát từ ga / muốn chơi quanh ga); phần lớn trip không có.
        var chosenStationIds = new[] { trip.StartStationId, trip.DestinationStationId }
            .Where(stationId => stationId.HasValue)
            .Select(stationId => stationId!.Value)
            .Distinct()
            .ToList();
        var chosenStations = chosenStationIds.Count == 0
            ? new Dictionary<Guid, StationRefDto>()
            : await dbContext.MetroStations
                .AsNoTracking()
                .Where(station => chosenStationIds.Contains(station.Id))
                .ToDictionaryAsync(
                    station => station.Id,
                    station => new StationRefDto(station.Order, station.Name),
                    cancellationToken);

        return new TripDetailReadModel(
            trip.Id,
            trip.Status,
            trip.StartLatitude,
            trip.StartLongitude,
            tripStation?.StationName,
            trip.DurationHours,
            trip.BudgetMin,
            trip.BudgetMax,
            tagIds,
            items
                .Select(item => new TripItemReadModel(
                    item.Id,
                    item.PlaceId,
                    item.PlaceName,
                    item.Category,
                    item.ImageUrl,
                    item.Latitude,
                    item.Longitude,
                    stationsByItemId.GetValueOrDefault(item.Id)?.StationName,
                    item.OrderIndex,
                    item.ScheduledTime,
                    item.EstimatedDurationMinutes,
                    item.EstimatedBudget,
                    item.Reasoning,
                    item.IsVisited,
                    item.VisitedAt,
                    item.Address,
                    stationsByItemId.GetValueOrDefault(item.Id)?.StationOrder,
                    stationsByItemId.GetValueOrDefault(item.Id)?.DistanceMeters))
                .ToList(),
            trip.CreatedAt,
            trip.UpdatedAt,
            trip.FinalizedAt,
            trip.TravelMode,
            trip.PlannedStartAt,
            tripStation?.StationOrder,
            tripStation?.DistanceMeters,
            trip.StartStationId is { } startStationId ? chosenStations.GetValueOrDefault(startStationId) : null,
            trip.DestinationStationId is { } destinationStationId
                ? chosenStations.GetValueOrDefault(destinationStationId)
                : null,
            trip.Note,
            trip.NoteApplied);
    }

    public async Task AddAsync(
        Trip trip,
        CancellationToken cancellationToken = default)
    {
        dbContext.Trips.Add(trip);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SoftDeleteAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var rowsChanged = await dbContext.Trips
            .Where(trip => trip.Id == tripId && trip.UserId == userId && trip.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(trip => trip.DeletedAt, (DateTime?)now)
                .SetProperty(trip => trip.UpdatedAt, now), cancellationToken);

        return rowsChanged == 1;
    }

    private sealed record TripStationRow(Guid TripId, string StationName);

}
