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
        CancellationToken cancellationToken = default) =>
        await dbContext.Trips
            .AsNoTracking()
            .Where(trip => trip.UserId == userId)
            .OrderByDescending(trip => trip.UpdatedAt)
            .ThenByDescending(trip => trip.CreatedAt)
            .ThenBy(trip => trip.Id)
            .Select(trip => new MyTripReadModel(
                trip.Id,
                trip.Status,
                trip.StartLatitude,
                trip.StartLongitude,
                trip.DurationHours,
                trip.BudgetMin,
                trip.BudgetMax,
                trip.Items.Count,
                trip.CreatedAt,
                trip.UpdatedAt))
            .ToListAsync(cancellationToken);

    public Task<Trip?> GetByIdAsync(
        Guid tripId,
        CancellationToken cancellationToken = default) =>
        dbContext.Trips
            .AsNoTracking()
            .SingleOrDefaultAsync(trip => trip.Id == tripId, cancellationToken);

    public async Task<bool> AttachUserIfUnownedAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var updatedAt = DateTime.UtcNow;
        var rowsChanged = await dbContext.Trips
            .Where(trip => trip.Id == tripId && trip.UserId == null)
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
                           && trip.Status == TripStatus.Draft)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(trip => trip.Status, TripStatus.Finalized)
                .SetProperty(trip => trip.UpdatedAt, updatedAt), cancellationToken);

        return rowsChanged == 1;
    }

    public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.ItineraryItems
            .AsNoTracking()
            .Where(item => item.Id == itemId && item.Trip.UserId == userId)
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
                           && item.Trip.Status == TripStatus.Finalized
                           && !item.IsVisited)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsVisited, true)
                .SetProperty(item => item.VisitedAt, visitedAt)
                .SetProperty(item => item.UpdatedAt, visitedAt.UtcDateTime), cancellationToken);

        return rowsChanged == 1;
    }
}
