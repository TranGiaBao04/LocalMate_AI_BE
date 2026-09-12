using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripOriginResolverService
{
    Task<TripOriginResolution?> ResolveAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default);
}
