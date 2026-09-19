using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ITripRepository
{
    Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Trip?> GetByIdAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);

    Task<bool> AttachUserIfUnownedAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> FinalizeTripAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkItineraryItemVisitedIfEligibleAsync(
        Guid itemId,
        Guid userId,
        DateTimeOffset visitedAt,
        CancellationToken cancellationToken = default);
}
