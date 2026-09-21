using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripAlternativesService
{
    Task<TripAlternativesResult> GetAlternativesAsync(
        Guid userId,
        Guid tripId,
        Guid itemId,
        int? limit,
        CancellationToken cancellationToken = default);
}
