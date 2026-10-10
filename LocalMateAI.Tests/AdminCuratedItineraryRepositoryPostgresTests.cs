using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AdminCuratedItineraryRepositoryPostgresTests
{
    // Hai ga cách nhau ≈ 853 m (1/128 độ kinh ở vĩ độ 10,75).
    private const double Lat = 10.75;
    private const double Station1Lng = 106.5;
    private const double Station2Lng = 106.5078125;

    [Fact]
    public async Task SoftDelete_HidesItineraryFromPublicReads_KeepsItsRows_AndFindsNothingTheSecondTime()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();

        var place = NewPlace("Quán", Station1Lng, PlaceStatus.Active, PlaceCategory.Cafe, 0, 0);
        var kept = Itinerary("Bến Thành buổi sáng", place);
        var removed = Itinerary("Bến Thành buổi tối", place);
        c.CuratedItineraries.AddRange(kept, removed);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var repository = new AdminCuratedItineraryRepository(c);

        Assert.True(await repository.SoftDeleteAsync(removed.Id));
        Assert.False(await repository.SoftDeleteAsync(removed.Id));
        Assert.False(await repository.SoftDeleteAsync(Guid.NewGuid()));

        var publicRepository = new CuratedItineraryRepository(c);
        Assert.Equal(kept.Id, Assert.Single(await publicRepository.GetAllAsync()).Id);
        Assert.Null(await publicRepository.GetForApplyAsync(removed.Id));
        Assert.NotNull(await publicRepository.GetForApplyAsync(kept.Id));
        Assert.Equal(
            kept.Id,
            Assert.Single(await new SearchRepository(c).SearchCuratedItinerariesAsync("ben thanh", 10)).Id);

        // Xoá mềm: dòng và chặng vẫn còn trong DB.
        Assert.NotNull(await c.CuratedItineraries
            .Where(itinerary => itinerary.Id == removed.Id)
            .Select(itinerary => itinerary.DeletedAt)
            .SingleAsync());
        Assert.Equal(1, await c.CuratedItineraryItems.CountAsync(item => item.CuratedItineraryId == removed.Id));
    }

    [Fact]
    public async Task Reads_SkipDeleted_SearchWithoutAccents_SortAndPage_AndDescribeEveryStop()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        c.MetroStations.AddRange(
            new MetroStation { Name = "Ga 1", Order = 1, Location = Point(Station1Lng) },
            new MetroStation { Name = "Ga 2", Order = 2, Location = Point(Station2Lng) });

        var pendingNearStation1 = NewPlace(
            "Quán chờ duyệt", Station1Lng + 0.0001, PlaceStatus.Pending, PlaceCategory.Food, 0, 50000);
        var activeNearStation2 = NewPlace(
            "Quán đang mở", Station2Lng + 0.0001, PlaceStatus.Active, PlaceCategory.Cafe, 10000, 20000);
        var deletedPlace = NewPlace(
            "Quán đã xoá", Station2Lng, PlaceStatus.Inactive, PlaceCategory.Cafe, 0, 0, DateTime.UtcNow);

        var morning = Itinerary("Sài Gòn buổi sáng", pendingNearStation1, activeNearStation2);
        var lunch = Itinerary("Bữa trưa quận 1", activeNearStation2, deletedPlace);
        var removed = Itinerary("Sài Gòn đã xoá", activeNearStation2);
        removed.DeletedAt = DateTime.UtcNow;
        c.CuratedItineraries.AddRange(morning, lunch, removed);
        await c.SaveChangesAsync();
        // Cho "Bữa trưa" là lịch sửa gần nhất để kiểm thứ tự mặc định.
        await c.CuratedItineraries
            .Where(itinerary => itinerary.Id == lunch.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(itinerary => itinerary.UpdatedAt, DateTime.UtcNow.AddMinutes(5)));
        c.ChangeTracker.Clear();

        var repository = new AdminCuratedItineraryRepository(c);

        var byDefault = await repository.GetPagedAsync(new AdminCuratedItineraryQuery());
        Assert.Equal(2, byDefault.TotalCount);
        Assert.Equal(["Bữa trưa quận 1", "Sài Gòn buổi sáng"], byDefault.Items.Select(itinerary => itinerary.Title));

        var byTitleDescending = await repository.GetPagedAsync(
            new AdminCuratedItineraryQuery { SortBy = "title", SortDirection = "desc" });
        Assert.Equal(
            ["Sài Gòn buổi sáng", "Bữa trưa quận 1"],
            byTitleDescending.Items.Select(itinerary => itinerary.Title));

        var secondPage = await repository.GetPagedAsync(new AdminCuratedItineraryQuery { Page = 2, PageSize = 1 });
        Assert.Equal("Sài Gòn buổi sáng", Assert.Single(secondPage.Items).Title);
        Assert.Equal(2, secondPage.TotalPages);

        // Lịch đã xoá cũng có "Sài Gòn" trong tiêu đề nhưng không được trả.
        var found = Assert.Single(
            (await repository.GetPagedAsync(new AdminCuratedItineraryQuery { Search = " SAI gon " })).Items);
        Assert.Equal(morning.Id, found.Id);
        Assert.Equal("Ga 1", found.StationName);
        Assert.Equal(1, found.UnavailablePlaceCount);
        Assert.Equal(["Quán chờ duyệt", "Quán đang mở"], found.Items.Select(item => item.Name));
        Assert.Equal([0, 1], found.Items.Select(item => item.OrderIndex));
        Assert.Equal(["Pending", "Active"], found.Items.Select(item => item.Status));
        Assert.Equal([false, false], found.Items.Select(item => item.IsDeleted));
        Assert.Equal(["Food", "Cafe"], found.Items.Select(item => item.Category));
        Assert.Equal([50000m, 20000m], found.Items.Select(item => item.EstimatedCostMax));

        var detail = await repository.GetByIdAsync(lunch.Id);
        Assert.NotNull(detail);
        Assert.Equal("Ga 2", detail.StationName);
        // Chặng có địa điểm đã xoá mềm vẫn hiện cho admin và được tính là không dùng được.
        Assert.Equal(1, detail.UnavailablePlaceCount);
        Assert.Equal(["Active", "Inactive"], detail.Items.Select(item => item.Status));
        // Cờ này cho FE biết chặng "Inactive" là đã xoá (phải bỏ ra) chứ không phải đang ẩn (bật lại được).
        Assert.Equal([false, true], detail.Items.Select(item => item.IsDeleted));

        Assert.Null(await repository.GetByIdAsync(removed.Id));
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddAndUpdate_WriteContentAndStopsInOrder_AndUpdateSkipsMissingOrDeletedItineraries()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();

        var cafe = NewPlace("Cà phê", Station1Lng, PlaceStatus.Active, PlaceCategory.Cafe, 10000, 20000);
        var museum = NewPlace("Bảo tàng", Station2Lng, PlaceStatus.Active, PlaceCategory.Culture, 0, 50000);
        var market = NewPlace("Chợ", Station1Lng + 0.001, PlaceStatus.Pending, PlaceCategory.CheckIn, 0, 0);
        var deletedPlace = NewPlace(
            "Quán đã xoá", Station2Lng, PlaceStatus.Inactive, PlaceCategory.Food, 0, 0, DateTime.UtcNow);
        c.Places.AddRange(cafe, museum, market, deletedPlace);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var repository = new AdminCuratedItineraryRepository(c);

        // Id không có trong DB thì vắng mặt; địa điểm chưa Active hoặc đã xoá mềm vẫn được trả kèm trạng thái.
        var candidates = await repository.GetPlacesAsync([cafe.Id, market.Id, deletedPlace.Id, Guid.NewGuid()]);
        Assert.Equal(3, candidates.Count);
        Assert.Equal(
            new CuratedPlaceCandidate(
                cafe.Id, "Cà phê", PlaceStatus.Active, false, PlaceCategory.Cafe, Lat, Station1Lng, 10000, 20000),
            candidates.Single(place => place.Id == cafe.Id));
        Assert.Equal(PlaceStatus.Pending, candidates.Single(place => place.Id == market.Id).Status);
        Assert.True(candidates.Single(place => place.Id == deletedPlace.Id).IsDeleted);

        var id = await repository.AddAsync(
            new CuratedItineraryDraft("Lịch A", "Mô tả", null, 150, 10000, 70000, [cafe.Id, museum.Id]));
        c.ChangeTracker.Clear();

        var created = await repository.GetByIdAsync(id);
        Assert.NotNull(created);
        Assert.Equal("Lịch A", created.Title);
        Assert.Equal("Mô tả", created.Description);
        Assert.Equal(150, created.EstimatedDurationMinutes);
        Assert.Equal(["Cà phê", "Bảo tàng"], created.Items.Select(item => item.Name));

        // Thay 2 chặng bằng 3 chặng theo thứ tự khác: không được đụng unique index (CuratedItineraryId, OrderIndex).
        Assert.True(await repository.UpdateAsync(
            id,
            new CuratedItineraryDraft("Lịch B", null, "https://img.test/b.jpg", 200, 0, 90000, [market.Id, museum.Id, cafe.Id])));
        c.ChangeTracker.Clear();

        var updated = await repository.GetByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("Lịch B", updated.Title);
        Assert.Null(updated.Description);
        Assert.Equal("https://img.test/b.jpg", updated.CoverImageUrl);
        Assert.Equal(200, updated.EstimatedDurationMinutes);
        Assert.Equal(90000m, updated.EstimatedCostMax);
        Assert.Equal(["Chợ", "Bảo tàng", "Cà phê"], updated.Items.Select(item => item.Name));
        Assert.Equal([0, 1, 2], updated.Items.Select(item => item.OrderIndex));
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        var other = new CuratedItineraryDraft("Không được ghi", null, null, 1, 0, 0, [cafe.Id, museum.Id]);
        Assert.False(await repository.UpdateAsync(Guid.NewGuid(), other));

        Assert.True(await repository.SoftDeleteAsync(id));
        Assert.False(await repository.UpdateAsync(id, other));
        // Lần sửa bị từ chối không đụng tới tiêu đề hay các chặng.
        Assert.Equal(
            "Lịch B",
            await c.CuratedItineraries.Where(itinerary => itinerary.Id == id).Select(itinerary => itinerary.Title).SingleAsync());
        Assert.Equal(3, await c.CuratedItineraryItems.CountAsync(item => item.CuratedItineraryId == id));
    }

    [Fact]
    public async Task ConcurrentUpdates_EndWithOneCompleteVersion()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();

        var places = Enumerable.Range(0, 6)
            .Select(index => NewPlace(
                $"Quán {index}", Station1Lng + index * 0.0001, PlaceStatus.Active, PlaceCategory.Cafe, 0, 0))
            .ToArray();
        c.Places.AddRange(places);
        await c.SaveChangesAsync();
        var id = await new AdminCuratedItineraryRepository(c).AddAsync(
            new CuratedItineraryDraft("Gốc", null, null, 120, 0, 0, [places[0].Id, places[1].Id]));
        c.ChangeTracker.Clear();

        var first = new CuratedItineraryDraft(
            "Bản một", null, null, 180, 0, 0, [places[2].Id, places[3].Id, places[4].Id]);
        var second = new CuratedItineraryDraft(
            "Bản hai", null, null, 240, 0, 0, [places[5].Id, places[4].Id, places[3].Id, places[2].Id]);

        await using var firstContext = db.Context();
        await using var secondContext = db.Context();
        var results = await Task.WhenAll(
            new AdminCuratedItineraryRepository(firstContext).UpdateAsync(id, first),
            new AdminCuratedItineraryRepository(secondContext).UpdateAsync(id, second));

        Assert.All(results, Assert.True);

        // Kết quả cuối là trọn vẹn một trong hai bản: tiêu đề, thời lượng và các chặng đi cùng nhau.
        var final = await new AdminCuratedItineraryRepository(c).GetByIdAsync(id);
        Assert.NotNull(final);
        var winner = final.Title == first.Title ? first : second;
        Assert.Equal(winner.Title, final.Title);
        Assert.Equal(winner.EstimatedDurationMinutes, final.EstimatedDurationMinutes);
        Assert.Equal(winner.PlaceIds, final.Items.Select(item => item.PlaceId));
        Assert.Equal(Enumerable.Range(0, winner.PlaceIds.Count), final.Items.Select(item => item.OrderIndex));
    }

    private static Point Point(double lng) => new(lng, Lat) { SRID = 4326 };

    private static Place NewPlace(
        string name,
        double lng,
        PlaceStatus status,
        PlaceCategory category,
        decimal costMin,
        decimal costMax,
        DateTime? deletedAt = null) => new()
    {
        Name = name,
        Address = "Test",
        Location = Point(lng),
        Category = category,
        Status = status,
        EstimatedCostMin = costMin,
        EstimatedCostMax = costMax,
        DeletedAt = deletedAt
    };

    private static CuratedItinerary Itinerary(string title, params Place[] places) => new()
    {
        Title = title,
        Items = places.Select((place, index) => new CuratedItineraryItem { Place = place, OrderIndex = index }).ToList()
    };
}
