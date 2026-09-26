using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripOriginResolverService
{
    Task<TripOriginResolution?> ResolveAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default);

    Task<TripOriginResolution?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}
