using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class TripRepository(AppDbContext dbContext) : ITripRepository
{
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
                trip.FinalizedAt
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
                trip.FinalizedAt))
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
            DurationHours = source.DurationHours,
            BudgetMin = source.BudgetMin,
            BudgetMax = source.BudgetMax,
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
                candidate.StartLatitude,
                candidate.StartLongitude,
                candidate.DurationHours,
                candidate.BudgetMin,
                candidate.BudgetMax,
                candidate.CreatedAt,
                candidate.UpdatedAt,
                candidate.FinalizedAt
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

        // Ga gần nhất (đường chim bay, geography — cùng cách tính với MetroStationRepository.FindNearestAsync).
        var tripStation = await dbContext.Database.SqlQuery<string>(
                $"""
                 SELECT ms."Name" AS "Value"
                 FROM "MetroStations" ms
                 ORDER BY ST_Distance(
                     ST_SetSRID(ST_MakePoint({trip.StartLongitude}, {trip.StartLatitude}), 4326)::geography,
                     ms."Location"::geography)
                 LIMIT 1
                 """)
            .FirstOrDefaultAsync(cancellationToken);

        var stationsByItemId = (await dbContext.Database.SqlQuery<ItemStationRow>(
                $"""
                 SELECT i."Id" AS "ItemId", s."Name" AS "StationName"
                 FROM "ItineraryItems" i
                 JOIN "Places" p ON p."Id" = i."PlaceId"
                 CROSS JOIN LATERAL (
                     SELECT ms."Name"
                     FROM "MetroStations" ms
                     ORDER BY ST_Distance(p."Location"::geography, ms."Location"::geography)
                     LIMIT 1
                 ) s
                 WHERE i."TripId" = {tripId}
                 """)
            .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ItemId, row => row.StationName);

        return new TripDetailReadModel(
            trip.Id,
            trip.Status,
            trip.StartLatitude,
            trip.StartLongitude,
            tripStation,
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
                    stationsByItemId.GetValueOrDefault(item.Id),
                    item.OrderIndex,
                    item.ScheduledTime,
                    item.EstimatedDurationMinutes,
                    item.EstimatedBudget,
                    item.Reasoning,
                    item.IsVisited,
                    item.VisitedAt))
                .ToList(),
            trip.CreatedAt,
            trip.UpdatedAt,
            trip.FinalizedAt);
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

    private sealed record ItemStationRow(Guid ItemId, string StationName);
}
