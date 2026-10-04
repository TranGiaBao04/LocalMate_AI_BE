using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

/// <summary>Dữ liệu ga mà chi tiết trip và xoá chặng đọc từ DB để tính đoạn đi (Phần 3 chế độ Metro).</summary>
public sealed class TripStationReadPostgresTests
{
    // Hai ga cách nhau 0,02° kinh (~2,2 km) trên cùng vĩ độ; 0,001° kinh ≈ 109 m ở vĩ độ 10,75.
    private const double Lat = 10.75;
    private const double Station1Lng = 106.50;
    private const double Station2Lng = 106.52;

    [Fact]
    public async Task Detail_ReportsBoardingStationItemStationsAndChosenStations()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c, TravelMode.Metro);

        var detail = await new TripRepository(c).GetOwnedDetailAsync(seed.TripId, seed.UserId);

        Assert.Equal("Ga 1", detail!.StationName);
        Assert.Equal(1, detail.StationOrder);
        Assert.InRange(detail.DistanceToStationMeters!.Value, 45, 65); // xuất phát cách ga 1 0,0005° kinh
        Assert.Null(detail.StartStation);
        Assert.Equal(new StationRefDto(2, "Ga 2"), detail.DestinationStation);

        Assert.Equal([1, 2, 2], detail.Items.Select(item => item.StationOrder));
        Assert.Equal(["Ga 1", "Ga 2", "Ga 2"], detail.Items.Select(item => item.StationName));
        Assert.All(detail.Items.Take(2), item => Assert.InRange(item.DistanceFromStationMeters!.Value, 100, 120));
        Assert.InRange(detail.Items[2].DistanceFromStationMeters!.Value, 205, 235);
    }

    [Fact]
    public async Task DeleteItem_MetroTrip_GivesRecalculatorStationsAndPlannedDate()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c, TravelMode.Metro);
        TimelineRecalculationInput? captured = null;

        var result = await new ItineraryItemRepository(c).DeleteItemAndRecalculateTimelineAsync(
            seed.TripId, seed.ItemIds[0], seed.UserId,
            input =>
            {
                captured = input;
                return [];
            });

        Assert.Equal(DeleteItineraryItemPersistenceStatus.Deleted, result.Status);
        Assert.Equal(TravelMode.Metro, captured!.TravelMode);
        Assert.Equal(new DateOnly(2026, 10, 10), captured.PlannedDate);
        Assert.Equal([2, 2], captured.RemainingItems.Select(item => item.StationOrder));
        Assert.All(captured.RemainingItems, item => Assert.True(item.DistanceFromStationMeters > 100));
    }

    [Fact]
    public async Task DeleteItem_OtherModes_DoNotLookUpStations()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c, TravelMode.Auto);
        TimelineRecalculationInput? captured = null;

        await new ItineraryItemRepository(c).DeleteItemAndRecalculateTimelineAsync(
            seed.TripId, seed.ItemIds[0], seed.UserId,
            input =>
            {
                captured = input;
                return [];
            });

        Assert.All(captured!.RemainingItems, item => Assert.Null(item.StationOrder));
    }

    [Fact]
    public async Task ReplaceItem_ChangesPlaceAndRecalculatesFollowingStopsInOneTransaction()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c, TravelMode.Auto);

        // Chặng đầu (gần ga 1) đổi sang địa điểm dự phòng gần ga 2. Lịch cũ 09:10 / 11:10 / 13:10, mỗi chặng 60'.
        var result = await new ItineraryItemRepository(c).ReplaceItemPlaceAndRecalculateTimelineAsync(
            seed.TripId, seed.ItemIds[0], seed.UserId, seed.SparePlaceId, 70_000,
            input => new ItineraryTimelineRecalculator().Recalculate(input, TripPlanningSettings.Default));

        // Chặng đầu giữ 09:10; tới chặng hai đi bộ 4' (0,28 km) ⇒ 10:14; tới chặng ba đi bộ 2' (0,14 km) ⇒ 11:16.
        Assert.Equal(ReplaceItemPersistenceStatus.Replaced, result.Status);
        Assert.Equal(
            [new TimeOnly(9, 10), new TimeOnly(10, 14), new TimeOnly(11, 16)],
            result.Items!.Select(item => item.ScheduledTime));
        Assert.Equal(seed.SparePlaceId, result.Items![0].PlaceId);
        Assert.Equal(70_000, result.Item!.EstimatedBudget);

        await using var verify = db.Context();
        var stored = await verify.ItineraryItems.AsNoTracking()
            .Where(item => item.TripId == seed.TripId).OrderBy(item => item.OrderIndex).ToListAsync();
        Assert.Equal(seed.SparePlaceId, stored[0].PlaceId);
        Assert.Null(stored[0].Reasoning);
        Assert.Equal([new TimeOnly(9, 10), new TimeOnly(10, 14), new TimeOnly(11, 16)],
            stored.Select(item => item.ScheduledTime));
    }

    [Fact]
    public async Task ReplaceItem_RecalculatorRejects_RollsBackThePlaceChange()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c, TravelMode.Auto);

        var result = await new ItineraryItemRepository(c).ReplaceItemPlaceAndRecalculateTimelineAsync(
            seed.TripId, seed.ItemIds[0], seed.UserId, seed.SparePlaceId, 70_000, _ => null);

        Assert.Equal(ReplaceItemPersistenceStatus.CrossesMidnight, result.Status);

        await using var verify = db.Context();
        var first = await verify.ItineraryItems.AsNoTracking().SingleAsync(item => item.Id == seed.ItemIds[0]);
        Assert.NotEqual(seed.SparePlaceId, first.PlaceId);
        Assert.Equal("Lý do cũ", first.Reasoning);
        Assert.Equal(new TimeOnly(9, 10), first.ScheduledTime);
    }

    [Fact]
    public async Task ReplaceItem_PlaceAlreadyInTrip_IsNotEligibleAndNeverRecalculates()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c, TravelMode.Auto);
        var placeOfSecondStop = await c.ItineraryItems.AsNoTracking()
            .Where(item => item.Id == seed.ItemIds[1]).Select(item => item.PlaceId).SingleAsync();
        var recalculated = false;

        var result = await new ItineraryItemRepository(c).ReplaceItemPlaceAndRecalculateTimelineAsync(
            seed.TripId, seed.ItemIds[0], seed.UserId, placeOfSecondStop, 70_000,
            _ =>
            {
                recalculated = true;
                return [];
            });

        Assert.Equal(ReplaceItemPersistenceStatus.NotEligible, result.Status);
        Assert.False(recalculated);
    }

    private static async Task<(Guid UserId, Guid TripId, IReadOnlyList<Guid> ItemIds, Guid SparePlaceId)> SeedAsync(
        AppDbContext c, TravelMode travelMode)
    {
        var station1 = new MetroStation { Name = "Ga 1", Order = 1, Location = Point(Station1Lng) };
        var station2 = new MetroStation { Name = "Ga 2", Order = 2, Location = Point(Station2Lng) };
        c.MetroStations.AddRange(station2, station1);

        var places = new[]
        {
            Place("Gần ga 1", Station1Lng + 0.001),
            Place("Gần ga 2", Station2Lng + 0.001),
            Place("Gần ga 2 hơn 200 m", Station2Lng + 0.002)
        };
        var spare = Place("Dự phòng gần ga 2", Station2Lng + 0.003); // không nằm trong trip, dùng để thay
        c.Places.AddRange(places);
        c.Places.Add(spare);

        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = new Trip
        {
            UserId = user.Id,
            StartLatitude = Lat,
            StartLongitude = Station1Lng - 0.0005,
            DurationHours = 6,
            BudgetMax = 500_000,
            TravelMode = travelMode,
            PlannedStartAt = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Unspecified),
            DestinationStationId = station2.Id,
            Items = places
                .Select((place, index) => new ItineraryItem
                {
                    PlaceId = place.Id,
                    OrderIndex = index,
                    ScheduledTime = new TimeOnly(9 + index * 2, 10),
                    EstimatedDurationMinutes = 60,
                    EstimatedBudget = 50_000,
                    Reasoning = "Lý do cũ"
                })
                .ToList()
        };
        c.Trips.Add(trip);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        return (user.Id, trip.Id, trip.Items.OrderBy(item => item.OrderIndex).Select(item => item.Id).ToList(),
            spare.Id);
    }

    private static Point Point(double lng) => new(lng, Lat) { SRID = 4326 };

    private static Place Place(string name, double lng) => new()
    {
        Name = name,
        Address = "Test",
        Location = Point(lng),
        Category = PlaceCategory.Cafe,
        Status = PlaceStatus.Active
    };
}
