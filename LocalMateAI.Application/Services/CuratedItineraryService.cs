using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class CuratedItineraryService(ICuratedItineraryRepository repository) : ICuratedItineraryService
{
    public Task<IReadOnlyList<CuratedItineraryResponse>> GetCuratedItinerariesAsync(
        CancellationToken cancellationToken = default)
        => repository.GetAllAsync(cancellationToken);
}
