using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IFeedbackRepository
{
    Task<Trip?> GetOwnedTripAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default);

    Task<bool> TryAddAsync(
        Feedback feedback,
        CancellationToken cancellationToken = default);
}
