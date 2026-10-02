using LocalMateAI.Application.DTOs.Stations;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminStationServiceTests
{
    private static readonly Guid BenThanh = Guid.NewGuid();
    private static readonly Guid OperaHouse = Guid.NewGuid();

    [Fact]
    public async Task BuildsFullMatrix_WithZeros_AndOutsideCoverage()
    {
        var repository = new FakeRepository(new AdminStationSnapshot(
            [Station(BenThanh, 1, "Bến Thành"), Station(OperaHouse, 2, "Nhà hát Thành phố")],
            [
                new(BenThanh, PlaceCategory.Cafe, PlaceStatus.Active, 2),
                new(BenThanh, PlaceCategory.Food, PlaceStatus.Pending, 1),
                new(BenThanh, PlaceCategory.Food, PlaceStatus.Inactive, 1),
                new(null, PlaceCategory.CheckIn, PlaceStatus.Active, 3)
            ]));

        var response = await new AdminStationService(repository, new FakeSystemSettingProvider()).GetStationsAsync();

        Assert.Equal(800, response.RadiusMeters);
        Assert.Equal(new PlaceStatusCounts(0, 3, 0, 3), response.OutsideCoverage);

        var benThanh = response.Stations[0];
        Assert.Equal(new PlaceStatusCounts(1, 2, 1, 4), benThanh.Totals);
        Assert.Equal(
            [
                new StationCategoryCounts(PlaceCategory.Cafe, 0, 2, 0, 2),
                new StationCategoryCounts(PlaceCategory.Food, 1, 0, 1, 2),
                new StationCategoryCounts(PlaceCategory.Culture, 0, 0, 0, 0),
                new StationCategoryCounts(PlaceCategory.CheckIn, 0, 0, 0, 0)
            ],
            benThanh.Categories);

        var opera = response.Stations[1];
        Assert.Equal(new PlaceStatusCounts(0, 0, 0, 0), opera.Totals);
        Assert.Equal(4, opera.Categories.Count);
        Assert.All(opera.Categories, category => Assert.Equal(0, category.Total));
    }

    [Fact]
    public async Task SortsStationsByOrder_AndPassesMatchingRadius()
    {
        var repository = new FakeRepository(new AdminStationSnapshot(
            [Station(OperaHouse, 2, "Nhà hát Thành phố"), Station(BenThanh, 1, "Bến Thành")], []));

        var response = await new AdminStationService(repository, new FakeSystemSettingProvider()).GetStationsAsync();

        Assert.Equal([1, 2], response.Stations.Select(station => station.Order));
        Assert.Equal(MetroClusterMatchingService.CandidateRadiusMeters, repository.LastRadius);
    }

    [Fact]
    public async Task NoStations_ReturnsEmptyList_AndCountsEverythingOutside()
    {
        var repository = new FakeRepository(new AdminStationSnapshot(
            [], [new(null, PlaceCategory.Cafe, PlaceStatus.Pending, 2)]));

        var response = await new AdminStationService(repository, new FakeSystemSettingProvider()).GetStationsAsync();

        Assert.Empty(response.Stations);
        Assert.Equal(new PlaceStatusCounts(2, 0, 0, 2), response.OutsideCoverage);
    }

    [Fact]
    public async Task FlagsUnderstockedStations_UsingActivePlacesOnly()
    {
        var repository = new FakeRepository(new AdminStationSnapshot(
            [Station(BenThanh, 1, "Bến Thành"), Station(OperaHouse, 2, "Nhà hát Thành phố")],
            [
                // Bến Thành: đúng 5 Active ⇒ đủ.
                new(BenThanh, PlaceCategory.Cafe, PlaceStatus.Active, 2),
                new(BenThanh, PlaceCategory.Food, PlaceStatus.Active, 3),
                // Nhà hát: 10 Pending + 2 Active ⇒ vẫn thiếu 3 (Pending không tính).
                new(OperaHouse, PlaceCategory.Culture, PlaceStatus.Pending, 10),
                new(OperaHouse, PlaceCategory.CheckIn, PlaceStatus.Active, 2)
            ]));

        var response = await new AdminStationService(repository, new FakeSystemSettingProvider()).GetStationsAsync();

        Assert.Equal(5, response.MinActivePlacesPerStation);
        Assert.Equal(1, response.UnderstockedStationCount);

        var benThanh = response.Stations[0];
        Assert.False(benThanh.IsUnderstocked);
        Assert.Equal(0, benThanh.Shortfall);
        Assert.Equal([PlaceCategory.Culture, PlaceCategory.CheckIn], benThanh.MissingCategories);

        var opera = response.Stations[1];
        Assert.True(opera.IsUnderstocked);
        Assert.Equal(3, opera.Shortfall);
        Assert.Equal([PlaceCategory.Cafe, PlaceCategory.Food, PlaceCategory.Culture], opera.MissingCategories);
    }

    [Fact]
    public async Task EmptyStation_IsUnderstockedByFullThreshold_AndMissesEveryCategory()
    {
        var repository = new FakeRepository(new AdminStationSnapshot([Station(BenThanh, 1, "Bến Thành")], []));
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.MinActivePlacesPerStation, 8);

        var response = await new AdminStationService(repository, settings).GetStationsAsync();

        var station = Assert.Single(response.Stations);
        Assert.True(station.IsUnderstocked);
        Assert.Equal(8, station.Shortfall);
        Assert.Equal(Enum.GetValues<PlaceCategory>(), station.MissingCategories);
        Assert.Equal([SystemSettingKeys.MinActivePlacesPerStation], settings.RequestedKeys);
    }

    private static AdminStationReadModel Station(Guid id, int order, string name) =>
        new(id, order, name, 10.77, 106.70);

    private sealed class FakeRepository(AdminStationSnapshot snapshot) : IAdminStationRepository
    {
        public double? LastRadius { get; private set; }

        public Task<AdminStationSnapshot> GetStationPlaceCountsAsync(double radiusMeters,
            CancellationToken cancellationToken = default)
        {
            LastRadius = radiusMeters;
            return Task.FromResult(snapshot);
        }
    }
}
