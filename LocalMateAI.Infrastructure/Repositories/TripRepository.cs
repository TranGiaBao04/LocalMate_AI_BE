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
}