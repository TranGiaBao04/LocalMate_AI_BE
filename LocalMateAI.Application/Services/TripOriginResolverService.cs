using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripOriginResolverService : ITripOriginResolverService
{
    private const double MaxServiceAreaDistanceMeters = 3000;

    private readonly IGeoService _geoService;

    public TripOriginResolverService(IGeoService geoService)
    {
        _geoService = geoService;
    }

    public async Task<TripOriginResolution?> ResolveAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var nearestStation = await _geoService.FindNearestStationAsync(
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
