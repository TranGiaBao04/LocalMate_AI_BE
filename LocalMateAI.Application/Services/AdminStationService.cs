using LocalMateAI.Application.DTOs.Stations;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class AdminStationService(
    IAdminStationRepository repository,
    ISystemSettingProvider settings) : IAdminStationService
{
    // Cùng bán kính với matching để số "active" của mỗi ga đúng bằng số địa điểm matching dùng được.
    public const double RadiusMeters = MetroClusterMatchingService.CandidateRadiusMeters;

    // Không cache số đếm: admin vừa duyệt/xoá địa điểm phải thấy số mới ngay (ngưỡng thì provider tự cache).
    public async Task<AdminStationsResponse> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var minActive = await settings.GetIntAsync(SystemSettingKeys.MinActivePlacesPerStation, cancellationToken);
        var snapshot = await repository.GetStationPlaceCountsAsync(RadiusMeters, cancellationToken);

        var stations = snapshot.Stations
            .OrderBy(station => station.Order)
            .ThenBy(station => station.Name, StringComparer.Ordinal)
            .ThenBy(station => station.Id)
            .Select(station => ToResponse(station,
                BuildCategories(snapshot.Counts.Where(count => count.StationId == station.Id)), minActive))
            .ToList();

        var outsideCoverage = Sum(BuildCategories(snapshot.Counts.Where(count => count.StationId is null)));

        return new AdminStationsResponse(RadiusMeters, minActive,
            stations.Count(station => station.IsUnderstocked), outsideCoverage, stations);
    }

    // BE-92: chỉ xét địa điểm Active của chính ga (Pending chưa hiện cho user; không cộng ga ±1 như matching)
    // ⇒ cờ nghĩa là "ga này cần thu thập thêm", không phải "user ở đây không tạo được lịch".
    private static AdminStationResponse ToResponse(AdminStationReadModel station,
        List<StationCategoryCounts> categories, int minActive)
    {
        var totals = Sum(categories);
        var missing = categories
            .Where(category => category.Active == 0)
            .Select(category => category.Category)
            .ToList();

        return new AdminStationResponse(station.Id, station.Order, station.Name, station.Latitude,
            station.Longitude, totals, categories,
            IsUnderstocked: totals.Active < minActive,
            Shortfall: Math.Max(0, minActive - totals.Active),
            MissingCategories: missing);
    }

    // Luôn đủ 4 category theo thứ tự enum, ô không có địa điểm = 0.
    private static List<StationCategoryCounts> BuildCategories(IEnumerable<StationPlaceCount> counts)
    {
        var list = counts.ToList();

        return Enum.GetValues<PlaceCategory>()
            .Select(category =>
            {
                int CountOf(PlaceStatus status) => list
                    .Where(count => count.Category == category && count.Status == status)
                    .Sum(count => count.Count);

                var pending = CountOf(PlaceStatus.Pending);
                var active = CountOf(PlaceStatus.Active);
                var inactive = CountOf(PlaceStatus.Inactive);
                return new StationCategoryCounts(category, pending, active, inactive, pending + active + inactive);
            })
            .ToList();
    }

    private static PlaceStatusCounts Sum(IReadOnlyCollection<StationCategoryCounts> categories) => new(
        categories.Sum(category => category.Pending),
        categories.Sum(category => category.Active),
        categories.Sum(category => category.Inactive),
        categories.Sum(category => category.Total));
}
