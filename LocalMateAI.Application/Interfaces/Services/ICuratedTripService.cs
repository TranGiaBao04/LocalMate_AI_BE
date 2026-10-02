using LocalMateAI.Application.DTOs.Itineraries;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ICuratedTripService
{
    Task<ApplyCuratedItineraryResult> ApplyAsync(
        Guid userId,
        Guid curatedItineraryId,
        ApplyCuratedItineraryRequest? request,
        CancellationToken cancellationToken = default);
}
