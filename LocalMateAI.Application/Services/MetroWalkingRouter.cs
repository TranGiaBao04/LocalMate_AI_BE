namespace LocalMateAI.Application.Services;

public record MetroStationInfo(
    string Code,
    string Name,
    double Latitude,
    double Longitude,
    string Address
);

public record MetroWalkingRouteResult(
    MetroStationInfo NearestStation,
    double DistanceKm,
    double DistanceMeters,
    int WalkingDurationMinutes,
    bool IsWalkable, // Trong bán kính đi bộ hợp lý (<= 1.5 km)
    string MapDirectionsUrl
);

public interface IMetroWalkingRouter
{
    MetroWalkingRouteResult FindNearestMetroStationRoute(double lat, double lng, string? placeName = null);
    IReadOnlyList<MetroStationInfo> GetMetroLine1Stations();
}

public sealed class MetroWalkingRouter(IGoogleMapsUrlBuilderService mapsUrlBuilder) : IMetroWalkingRouter
{
    // Danh sách 14 ga Metro Số 1 (Bến Thành - Suối Tiên) chuẩn vị trí địa lý
    private static readonly List<MetroStationInfo> MetroLine1Stations =
    [
        new("GA-01", "Ga Bến Thành", 10.7712, 106.6983, "Quận 1, TP.HCM"),
        new("GA-02", "Ga Nhà hát Thành phố", 10.7764, 106.7025, "Đồng Khởi, Quận 1, TP.HCM"),
        new("GA-03", "Ga Ba Son", 10.7818, 106.7088, "Tôn Đức Thắng, Quận 1, TP.HCM"),
        new("GA-04", "Ga Công viên Văn Thánh", 10.7961, 106.7175, "Bình Thạnh, TP.HCM"),
        new("GA-05", "Ga Tân Cảng", 10.7989, 106.7231, "Bình Thạnh, TP.HCM"),
        new("GA-06", "Ga Thảo Điền", 10.8016, 106.7335, "TP. Thủ Đức, TP.HCM"),
        new("GA-07", "Ga An Phú", 10.8028, 106.7447, "TP. Thủ Đức, TP.HCM"),
        new("GA-08", "Ga Rạch Chiếc", 10.8105, 106.7602, "TP. Thủ Đức, TP.HCM"),
        new("GA-09", "Ga Phước Long", 10.8228, 106.7686, "TP. Thủ Đức, TP.HCM"),
        new("GA-10", "Ga Bình Thái", 10.8358, 106.7758, "TP. Thủ Đức, TP.HCM"),
        new("GA-11", "Ga Thủ Đức", 10.8467, 106.7725, "TP. Thủ Đức, TP.HCM"),
        new("GA-12", "Ga Khu Công nghệ cao", 10.8569, 106.7903, "TP. Thủ Đức, TP.HCM"),
        new("GA-13", "Ga Suối Tiên", 10.8653, 106.8022, "TP. Thủ Đức, TP.HCM"),
        new("GA-14", "Ga Bến xe Miền Đông mới", 10.8765, 106.8142, "TP. Thủ Đức, TP.HCM")
    ];

    public IReadOnlyList<MetroStationInfo> GetMetroLine1Stations() => MetroLine1Stations.AsReadOnly();

    public MetroWalkingRouteResult FindNearestMetroStationRoute(double lat, double lng, string? placeName = null)
    {
        MetroStationInfo nearest = MetroLine1Stations[0];
        double minDistance = double.MaxValue;

        foreach (var station in MetroLine1Stations)
        {
            var dist = CalculateHaversine(lat, lng, station.Latitude, station.Longitude);
            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = station;
            }
        }

        var roadDistanceKm = Math.Round(minDistance * 1.25, 2);
        var distanceMeters = Math.Round(roadDistanceKm * 1000, 0);
        var walkingMinutes = (int)Math.Max(1, Math.Ceiling((roadDistanceKm / 4.8) * 60));

        var directionsUrl = mapsUrlBuilder.BuildDirectionsUrl(
            nearest.Latitude, nearest.Longitude,
            lat, lng,
            nearest.Name, placeName,
            "walking");

        return new MetroWalkingRouteResult(
            NearestStation: nearest,
            DistanceKm: roadDistanceKm,
            DistanceMeters: distanceMeters,
            WalkingDurationMinutes: walkingMinutes,
            IsWalkable: roadDistanceKm <= 1.5,
            MapDirectionsUrl: directionsUrl
        );
    }

    private static double CalculateHaversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371.0;
        var dLat = (Math.PI / 180.0) * (lat2 - lat1);
        var dLon = (Math.PI / 180.0) * (lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos((Math.PI / 180.0) * lat1) * Math.Cos((Math.PI / 180.0) * lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
