using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class TripExplanationRepositoryPostgresTests
{
    private static readonly DateTime ExplainedAt = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetOwned_ReturnsStopsInOrder_WithDescriptionsAndActiveTagNames()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);

        var trip = await new TripExplanationRepository(c).GetOwnedAsync(seed.TripId, seed.UserId);

        Assert.Equal(seed.TripId, trip!.TripId);
        Assert.Equal(TripStatus.Draft, trip.Status);
        Assert.Equal(5, trip.DurationHours);
        Assert.Equal("muốn chỗ yên tĩnh", trip.Note);
        Assert.Equal(["Cà phê"], trip.InterestTagNames);

        Assert.Equal(["Tonkin", "Công viên"], trip.Stops.Select(stop => stop.PlaceName));
        Assert.Equal(seed.ItemIds, trip.Stops.Select(stop => stop.ItemId));
        Assert.Equal("Không gian yên tĩnh", trip.Stops[0].Description);
        Assert.Equal(PlaceCategory.Cafe, trip.Stops[0].Category);
        Assert.Equal(["Cà phê"], trip.Stops[0].TagNames);
        Assert.Equal(new TimeOnly(9, 10), trip.Stops[0].ScheduledTime);
        Assert.Equal(60, trip.Stops[0].EstimatedDurationMinutes);
        Assert.Null(trip.Stops[1].Description);
        Assert.Empty(trip.Stops[1].TagNames);
    }

    [Fact]
    public async Task GetOwned_ForeignOrDeletedTrip_ReturnsNull()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new TripExplanationRepository(c);

        Assert.Null(await repository.GetOwnedAsync(seed.TripId, Guid.NewGuid()));
        Assert.Null(await repository.GetOwnedAsync(Guid.NewGuid(), seed.UserId));

        await c.Trips.Where(trip => trip.Id == seed.TripId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(trip => trip.DeletedAt, DateTime.UtcNow));

        Assert.Null(await repository.GetOwnedAsync(seed.TripId, seed.UserId));
    }

    [Fact]
    public async Task Apply_WritesReasonsAndMarksTheTrip_AndDetailAndForkCarryTheMark()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);

        var written = await new TripExplanationRepository(c).ApplyAsync(
            seed.TripId,
            seed.UserId,
            [
                new TripExplanationUpdate(seed.ItemIds[0], seed.PlaceIds[0], "Quán yên tĩnh, hợp ngồi làm việc."),
                new TripExplanationUpdate(seed.ItemIds[1], seed.PlaceIds[1], "Thoáng mát, dễ chịu.")
            ],
            ExplainedAt);

        Assert.Equal(2, written!.Count);
        var trips = new TripRepository(c);
        var detail = await trips.GetOwnedDetailAsync(seed.TripId, seed.UserId);
        Assert.Equal(ExplainedAt, detail!.AiExplainedAt);
        Assert.Equal(
            ["Quán yên tĩnh, hợp ngồi làm việc.", "Thoáng mát, dễ chịu."],
            detail.Items.Select(item => item.Reasoning));

        var copy = await trips.ForkTripAsync(seed.TripId, seed.UserId);
        c.ChangeTracker.Clear();
        var copyDetail = await trips.GetOwnedDetailAsync(copy!.Id, seed.UserId);
        Assert.Equal(ExplainedAt, copyDetail!.AiExplainedAt);
        Assert.Equal("Quán yên tĩnh, hợp ngồi làm việc.", copyDetail.Items[0].Reasoning);
    }

    [Fact]
    public async Task Apply_SkipsStopsWhosePlaceChangedOrThatBelongToAnotherTrip()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);

        var written = await new TripExplanationRepository(c).ApplyAsync(
            seed.TripId,
            seed.UserId,
            [
                new TripExplanationUpdate(seed.ItemIds[0], seed.PlaceIds[0], "Lý do mới."),
                // Chặng thứ hai đã bị thay địa điểm: lý do viết cho địa điểm cũ không được ghi.
                new TripExplanationUpdate(seed.ItemIds[1], Guid.NewGuid(), "Lý do cho địa điểm đã bị thay."),
                new TripExplanationUpdate(Guid.NewGuid(), seed.PlaceIds[0], "Chặng không tồn tại.")
            ],
            ExplainedAt);

        Assert.Equal(seed.ItemIds[0], Assert.Single(written!).ItemId);
        var reasons = await c.ItineraryItems.AsNoTracking()
            .Where(item => item.TripId == seed.TripId)
            .OrderBy(item => item.OrderIndex)
            .Select(item => item.Reasoning)
            .ToListAsync();
        Assert.Equal(["Lý do mới.", "Lý do cũ"], reasons);
    }

    [Theory]
    [InlineData("finalized")]
    [InlineData("deleted")]
    [InlineData("foreign")]
    public async Task Apply_TripNoLongerAnOwnedDraft_ReturnsNullAndWritesNothing(string situation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var userId = seed.UserId;

        switch (situation)
        {
            case "finalized":
                await c.Trips.Where(trip => trip.Id == seed.TripId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(trip => trip.Status, TripStatus.Finalized));
                break;
            case "deleted":
                await c.Trips.Where(trip => trip.Id == seed.TripId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(trip => trip.DeletedAt, DateTime.UtcNow));
                break;
            default:
                userId = Guid.NewGuid();
                break;
        }

        var written = await new TripExplanationRepository(c).ApplyAsync(
            seed.TripId,
            userId,
            [new TripExplanationUpdate(seed.ItemIds[0], seed.PlaceIds[0], "Không được ghi.")],
            ExplainedAt);

        Assert.Null(written);
        Assert.Null(await c.Trips.AsNoTracking().Where(trip => trip.Id == seed.TripId).Select(trip => trip.AiExplainedAt).SingleAsync());
        Assert.All(
            await c.ItineraryItems.AsNoTracking().Where(item => item.TripId == seed.TripId).Select(item => item.Reasoning).ToListAsync(),
            reasoning => Assert.Equal("Lý do cũ", reasoning));
    }

    private static async Task<(Guid UserId, Guid TripId, IReadOnlyList<Guid> ItemIds, IReadOnlyList<Guid> PlaceIds)> SeedAsync(
        AppDbContext c)
    {
        c.MetroStations.Add(new MetroStation { Name = "Ga 1", Order = 1, Location = Point(106.50) });

        var coffeeTag = new Tag { Name = "Cà phê", Type = TagType.Interest };
        var retiredTag = new Tag { Name = "Tag đã tắt", Type = TagType.Interest, IsActive = false };
        var cafe = Place("Tonkin", 106.501, "Không gian yên tĩnh");
        cafe.Tags.Add(new PlaceTag { Tag = coffeeTag });
        cafe.Tags.Add(new PlaceTag { Tag = retiredTag });
        var park = Place("Công viên", 106.502, null);
        c.Places.AddRange(cafe, park);

        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = new Trip
        {
            UserId = user.Id,
            StartLatitude = 10.75,
            StartLongitude = 106.50,
            DurationHours = 5,
            BudgetMax = 500_000,
            Note = "muốn chỗ yên tĩnh",
            PlannedStartAt = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Unspecified),
            Tags = [new TripTag { Tag = coffeeTag }, new TripTag { Tag = retiredTag }],
            // Thêm theo thứ tự ngược để chắc chắn kết quả sắp theo OrderIndex chứ không theo thứ tự chèn.
            Items =
            [
                Item(park, 1, new TimeOnly(10, 20)),
                Item(cafe, 0, new TimeOnly(9, 10))
            ]
        };
        c.Trips.Add(trip);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var items = trip.Items.OrderBy(item => item.OrderIndex).ToList();
        return (user.Id, trip.Id, items.Select(item => item.Id).ToList(), items.Select(item => item.PlaceId).ToList());
    }

    private static ItineraryItem Item(Place place, int orderIndex, TimeOnly time) => new()
    {
        Place = place,
        OrderIndex = orderIndex,
        ScheduledTime = time,
        EstimatedDurationMinutes = 60,
        EstimatedBudget = 50_000,
        Reasoning = "Lý do cũ"
    };

    private static Point Point(double lng) => new(lng, 10.75) { SRID = 4326 };

    private static Place Place(string name, double lng, string? description) => new()
    {
        Name = name,
        Description = description,
        Address = "Test",
        Location = Point(lng),
        Category = PlaceCategory.Cafe,
        Status = PlaceStatus.Active
    };
}
