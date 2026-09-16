using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);

    Task<bool> AttachUserIfUnownedAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
