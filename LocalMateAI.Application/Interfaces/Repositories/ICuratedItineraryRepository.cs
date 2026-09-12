using LocalMateAI.Application.DTOs.Itineraries;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ICuratedItineraryRepository
{
    Task<IReadOnlyList<CuratedItineraryResponse>> GetAllAsync(CancellationToken cancellationToken = default);
}
