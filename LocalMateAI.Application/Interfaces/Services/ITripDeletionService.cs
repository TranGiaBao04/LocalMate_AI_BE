using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripDeletionService
{
    Task<DeleteTripResult> DeleteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default);
}
