using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class TripOriginResolverService(
    IGeoService geoService,
    ISystemSettingProvider settings) : ITripOriginResolverService
{
    public Task<TripOriginResolution?> ResolveAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(request.StartLatitude, request.StartLongitude, cancellationToken);

    public async Task<TripOriginResolution?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var nearestStation = await geoService.FindNearestStationAsync(
            latitude,
            longitude,
            cancellationToken);

        if (nearestStation is null)
        {
            return null;
        }

        // Ngưỡng đường chim bay tới ga gần nhất, admin chỉnh trong SystemSettings (mặc định 12 km).
        // Đây là cổng lọc thô, không phải quãng đường thật; nếu sau này có API chỉ đường, đổi sang tính theo phút.
        var maxDistanceMeters = await settings.GetIntAsync(
            SystemSettingKeys.MaxServiceAreaDistanceMeters,
            cancellationToken);

        return new TripOriginResolution(
            nearestStation,
            nearestStation.DistanceMeters <= maxDistanceMeters);
    }
}
