using LocalMateAI.Application.DTOs.Search;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class SearchRepositoryPostgresTests
{
    // Hai ga cách nhau ≈ 853 m (1/128 độ kinh ở vĩ độ 10,75); ga thứ ba ở rất xa.
    private const double Lat = 10.75;
    private const double TanBenLng = 106.5;
    private const double BenThanhLng = 106.5078125;
    private const double FarLng = 106.6;
    private const double Radius = 800;

    [Fact]
    public async Task Places_MatchWithoutAccents_RankByMatchedField_AndSkipHiddenOnes()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        AddStations(c);

        var taggedActive = Place("Quán A", BenThanhLng + 0.0002);
        taggedActive.Tags.Add(new PlaceTag { Tag = new Tag { Name = "Đặc sản Bến Thành", Type = TagType.Interest } });
        var taggedInactive = Place("Quán C", BenThanhLng + 0.0003);
        taggedInactive.Tags.Add(new PlaceTag
        {
            Tag = new Tag { Name = "Bến Thành cũ", Type = TagType.Interest, IsActive = false }
        });

        c.Places.AddRange(
            Place("Bến Thành Food", BenThanhLng + 0.0001),
            // Ngoài bán kính cụm ga ⇒ không có ga; đã xác minh ⇒ đứng trước trong cùng hạng.
            Place("Bến Thành xa", FarLng, isVerified: true),
            Place("Chợ Bến Thành", BenThanhLng - 0.0001),
            taggedActive,
            Place("Quán B", BenThanhLng - 0.0002, address: "12 Lê Lai, Bến Thành"),
            taggedInactive,
            Place("Bến Thành chờ duyệt", BenThanhLng, status: PlaceStatus.Pending),
            Place("Bến Thành đã xoá", BenThanhLng, status: PlaceStatus.Inactive, deletedAt: DateTime.UtcNow));
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var result = await new SearchRepository(c).SearchPlacesAsync("BEN thanh", 10, Radius);

        Assert.Equal(
            ["Bến Thành xa", "Bến Thành Food", "Chợ Bến Thành", "Quán A", "Quán B"],
            result.Select(place => place.Name));
        Assert.Equal(
            [SearchMatchField.Name, SearchMatchField.Name, SearchMatchField.Name, SearchMatchField.Tag, SearchMatchField.Address],
            result.Select(place => place.MatchedOn));
        Assert.Null(result[0].Station);
        Assert.All(result.Skip(1), place => Assert.Equal(new StationRefDto(2, "Bến Thành"), place.Station));

        Assert.Equal(2, (await new SearchRepository(c).SearchPlacesAsync("ben thanh", 2, Radius)).Count);
    }

    [Fact]
    public async Task Stations_PrefixMatchFirst_AndCountOnlyActivePlacesInsideCluster()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        AddStations(c);
        c.Places.AddRange(
            Place("Quán 1", BenThanhLng + 0.0001),
            Place("Quán 2", BenThanhLng - 0.0001),
            Place("Chờ duyệt", BenThanhLng, status: PlaceStatus.Pending),
            Place("Đã xoá", BenThanhLng, status: PlaceStatus.Inactive, deletedAt: DateTime.UtcNow),
            Place("Xa", FarLng));
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var repository = new SearchRepository(c);
        var result = await repository.SearchStationsAsync("ben", 10, Radius);

        // "Bến Thành" bắt đầu bằng từ khoá nên đứng trước dù Order lớn hơn "Tân Bến Cảng".
        Assert.Equal(["Bến Thành", "Tân Bến Cảng"], result.Select(station => station.Name));
        Assert.Equal([2, 1], result.Select(station => station.Order));
        Assert.Equal([2, 0], result.Select(station => station.PlaceCount));

        Assert.Equal("Bến Thành", Assert.Single(await repository.SearchStationsAsync("ben", 1, Radius)).Name);
        Assert.Empty(await repository.SearchStationsAsync("suoi tien", 10, Radius));
    }

    [Fact]
    public async Task PercentAndUnderscore_AreLiteralCharacters()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        c.Places.AddRange(
            Place("Giảm 100% hôm nay", BenThanhLng),
            Place("Giảm 1000 đồng", BenThanhLng),
            Place("Mã a_b", BenThanhLng),
            Place("Mã axb", BenThanhLng));
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var repository = new SearchRepository(c);

        var percent = Assert.Single(await repository.SearchPlacesAsync("100%", 10, Radius));
        Assert.Equal("Giảm 100% hôm nay", percent.Name);
        // Không có ga nào trong DB ⇒ không gán được ga.
        Assert.Null(percent.Station);
        Assert.Equal("Mã a_b", Assert.Single(await repository.SearchPlacesAsync("a_b", 10, Radius)).Name);
    }

    [Fact]
    public async Task CuratedItineraries_MatchTitleBeforeDescription_AndNeedAnActivePlace()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        AddStations(c);

        var pendingNearTanBen = Place("Chờ duyệt", TanBenLng, status: PlaceStatus.Pending);
        var activeNearBenThanh = Place("Đang mở", BenThanhLng + 0.0001);
        c.Places.AddRange(pendingNearTanBen, activeNearBenThanh);
        c.CuratedItineraries.AddRange(
            Itinerary("Dạo phố", "Đi bộ quanh chợ Bến Thành", activeNearBenThanh),
            // Chặng đầu chưa Active ⇒ ga lấy theo chặng Active đầu tiên.
            Itinerary("Bến Thành nửa ngày", null, pendingNearTanBen, activeNearBenThanh),
            Itinerary("Bến Thành bỏ hoang", null, pendingNearTanBen),
            Itinerary("Thảo Điền cuối tuần", null, activeNearBenThanh));
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var result = await new SearchRepository(c).SearchCuratedItinerariesAsync("ben thanh", 10);

        Assert.Equal(["Bến Thành nửa ngày", "Dạo phố"], result.Select(itinerary => itinerary.Title));
        Assert.All(result, itinerary => Assert.Equal("Bến Thành", itinerary.StationName));
    }

    private static void AddStations(AppDbContext context) =>
        context.MetroStations.AddRange(
            new MetroStation { Name = "Tân Bến Cảng", Order = 1, Location = Point(TanBenLng, Lat) },
            new MetroStation { Name = "Bến Thành", Order = 2, Location = Point(BenThanhLng, Lat) },
            new MetroStation { Name = "Ba Son", Order = 3, Location = Point(107.0, 11.0) });

    private static Point Point(double lng, double lat) => new(lng, lat) { SRID = 4326 };

    private static Place Place(
        string name,
        double lng,
        PlaceStatus status = PlaceStatus.Active,
        string address = "Test",
        bool isVerified = false,
        DateTime? deletedAt = null) => new()
    {
        Name = name,
        Address = address,
        Location = Point(lng, Lat),
        Category = PlaceCategory.Cafe,
        Status = status,
        IsVerified = isVerified,
        DeletedAt = deletedAt
    };

    private static CuratedItinerary Itinerary(string title, string? description, params Place[] places) => new()
    {
        Title = title,
        Description = description,
        Items = places.Select((place, index) => new CuratedItineraryItem { Place = place, OrderIndex = index }).ToList()
    };
}
