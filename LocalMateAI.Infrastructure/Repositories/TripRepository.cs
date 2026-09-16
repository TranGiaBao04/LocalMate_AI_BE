using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class TripRepository(AppDbContext dbContext) : ITripRepository
{
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
}
