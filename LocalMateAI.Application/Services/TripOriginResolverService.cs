using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripOriginResolverService(IGeoService geoService) : ITripOriginResolverService
{
    private const double MaxServiceAreaDistanceMeters = 3000;

    public async Task<TripOriginResolution?> ResolveAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var nearestStation = await geoService.FindNearestStationAsync(
            request.StartLatitude,
            request.StartLongitude,
            cancellationToken);

        if (nearestStation is null)
        {
            return null;
        }

        return new TripOriginResolution(
            nearestStation,
            nearestStation.DistanceMeters <= MaxServiceAreaDistanceMeters);
    }
}
