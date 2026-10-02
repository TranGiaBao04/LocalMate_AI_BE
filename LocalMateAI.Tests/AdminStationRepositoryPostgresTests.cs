using LocalMateAI.Application.DTOs.Stations;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AdminStationRepositoryPostgresTests
{
    // Toạ độ là số nhị phân chính xác (bội của 1/128 độ) để điểm giữa 2 ga cách đều tuyệt đối.
    // 1/128 độ kinh ≈ 853 m ở vĩ độ 10,75 ⇒ điểm giữa cách mỗi ga ≈ 427 m (< 800 m).
    private const double Lat = 10.75;
    private const double Station1Lng = 106.5;
    private const double Station2Lng = 106.5078125;
    private const double MidpointLng = 106.50390625;

    [Fact]
    public async Task AssignsNearestStationWithinRadius_SkipsDeleted_AndReportsOutsideCoverage()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();

        var station1 = new MetroStation { Name = "Ga 1", Order = 1, Location = Point(Station1Lng, Lat) };
        var station2 = new MetroStation { Name = "Ga 2", Order = 2, Location = Point(Station2Lng, Lat) };
        var station3 = new MetroStation { Name = "Ga 3", Order = 3, Location = Point(107.0, 11.0) };
        c.MetroStations.AddRange(station2, station3, station1);

        c.Places.AddRange(
            Place("Gần ga 1", Station1Lng + 0.0005, PlaceCategory.Cafe, PlaceStatus.Active),
            // Cách đều 2 ga ⇒ thuộc ga Order nhỏ hơn.
            Place("Điểm giữa", MidpointLng, PlaceCategory.Culture, PlaceStatus.Active),
            Place("Gần ga 2 chờ duyệt", Station2Lng + 0.0005, PlaceCategory.Food, PlaceStatus.Pending),
            Place("Gần ga 2 tạm tắt", Station2Lng - 0.0005, PlaceCategory.Food, PlaceStatus.Inactive),
            Place("Đã xoá", Station1Lng, PlaceCategory.Cafe, PlaceStatus.Inactive, DateTime.UtcNow),
            // ≈ 9,5 km tới ga 2 ⇒ ngoài vùng phủ.
            Place("Xa", 106.6, PlaceCategory.CheckIn, PlaceStatus.Active));
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var response = await new AdminStationService(new AdminStationRepository(c), new FakeSystemSettingProvider()).GetStationsAsync();

        Assert.Equal([1, 2, 3], response.Stations.Select(station => station.Order));
        Assert.Equal(new PlaceStatusCounts(0, 2, 0, 2), response.Stations[0].Totals);
        Assert.Equal(1, Category(response.Stations[0], PlaceCategory.Cafe).Active);
        Assert.Equal(1, Category(response.Stations[0], PlaceCategory.Culture).Active);
        Assert.Equal(new StationCategoryCounts(PlaceCategory.Food, 1, 0, 1, 2),
            Category(response.Stations[1], PlaceCategory.Food));
        Assert.Equal(new PlaceStatusCounts(0, 0, 0, 0), response.Stations[2].Totals);
        Assert.Equal(new PlaceStatusCounts(0, 1, 0, 1), response.OutsideCoverage);
        Assert.Equal(10.75, response.Stations[0].Latitude, 6);
        Assert.Equal(106.5, response.Stations[0].Longitude, 6);
    }

    [Fact]
    public async Task NoStations_EveryPlaceIsOutsideCoverage()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        c.Places.Add(Place("Lẻ loi", Station1Lng, PlaceCategory.Cafe, PlaceStatus.Pending));
        await c.SaveChangesAsync();

        var response = await new AdminStationService(new AdminStationRepository(c), new FakeSystemSettingProvider()).GetStationsAsync();

        Assert.Empty(response.Stations);
        Assert.Equal(new PlaceStatusCounts(1, 0, 0, 1), response.OutsideCoverage);
    }

    private static StationCategoryCounts Category(AdminStationResponse station, PlaceCategory category) =>
        station.Categories.Single(item => item.Category == category);

    private static Point Point(double lng, double lat) => new(lng, lat) { SRID = 4326 };

    private static Place Place(string name, double lng, PlaceCategory category, PlaceStatus status,
        DateTime? deletedAt = null) => new()
    {
        Name = name,
        Address = "Test",
        Location = Point(lng, Lat),
        Category = category,
        Status = status,
        DeletedAt = deletedAt
    };
}
