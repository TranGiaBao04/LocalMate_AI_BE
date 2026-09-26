using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripOriginResolverService(IGeoService geoService) : ITripOriginResolverService
{
    // Khoảng cách tối đa (mét, đường chim bay) từ điểm xuất phát tới ga gần nhất để còn được phục vụ.
    // 12 km ≈ 39 phút di chuyển (×1,3 hệ số đường vòng, 24 km/h) — phủ toàn bộ các quận nội thành,
    // TP Thủ Đức và Nhà Bè; không phủ Bình Chánh, Hóc Môn, Củ Chi, Cần Giờ.
    // Đây là cổng lọc thô, không phải quãng đường thật; nếu sau này có API chỉ đường, đổi sang tính theo phút.
    private const double MaxServiceAreaDistanceMeters = 12000;

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

        return new TripOriginResolution(
            nearestStation,
            nearestStation.DistanceMeters <= MaxServiceAreaDistanceMeters);
    }
}
