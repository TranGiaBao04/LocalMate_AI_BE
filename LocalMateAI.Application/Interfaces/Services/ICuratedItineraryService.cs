using LocalMateAI.Application.DTOs.Itineraries;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ICuratedItineraryService
{
    Task<IReadOnlyList<CuratedItineraryResponse>> GetCuratedItinerariesAsync(CancellationToken cancellationToken = default);
}
