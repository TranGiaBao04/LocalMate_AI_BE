using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class CuratedTripBuilderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime PlannedStart = new(2026, 10, 3, 8, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Build_SchedulesByCategoryDurationAndAddsTravelBetweenStops()
    {
        // Cùng vị trí nên mỗi đoạn đi tối thiểu 1 phút: 08:00, +60+1, +75+1.
        var source = Source(
            Place(0, 50_000m, PlaceCategory.Cafe),
            Place(1, 30_000m, PlaceCategory.Food),
            Place(2, 0m, PlaceCategory.Culture));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, PlannedStart, TravelMode.Auto);

        Assert.Equal(TripStatus.Draft, trip.Status);
        Assert.Equal(UserId, trip.UserId);
        var items = trip.Items.OrderBy(item => item.OrderIndex).ToList();
        Assert.Equal([0, 1, 2], items.Select(item => item.OrderIndex));
        Assert.Equal([60, 75, 90], items.Select(item => item.EstimatedDurationMinutes));
        Assert.Equal(
            [new TimeOnly(8, 0), new TimeOnly(9, 1), new TimeOnly(10, 17)],
            items.Select(item => item.ScheduledTime));
        Assert.Equal([50_000m, 30_000m, 0m], items.Select(item => item.EstimatedBudget));
        Assert.All(items, item => Assert.Equal(trip.Id, item.TripId));
        Assert.All(items, item => Assert.Contains("Lịch trình mẫu", item.Reasoning));
    }

    [Fact]
    public void Build_WithoutStartLocation_StartsAtFirstPlaceAndFirstStopAtStartTime()
    {
        var source = Source(Place(1, 10_000m, latitude: 10.90, longitude: 106.90), Place(0, 10_000m, latitude: 10.77, longitude: 106.70));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, PlannedStart, TravelMode.Auto);

        Assert.Equal(10.77, trip.StartLatitude);
        Assert.Equal(106.70, trip.StartLongitude);
        Assert.Equal(new TimeOnly(8, 0), trip.Items.OrderBy(item => item.OrderIndex).First().ScheduledTime);
    }

    [Fact]
    public void Build_WithStartLocation_AddsTravelFromOriginBeforeFirstStop()
    {
        // 0,02° vĩ độ ≈ 2,89 km đường bộ → Auto đi xe máy 8 phút.
        var source = Source(Place(0, 10_000m, latitude: 10.770));

        var trip = CuratedTripBuilder.Build(UserId, source, 10.750, 106.70, PlannedStart, TravelMode.Auto);

        Assert.Equal(10.750, trip.StartLatitude);
        Assert.Equal(106.70, trip.StartLongitude);
        Assert.Equal(new TimeOnly(8, 8), trip.Items.Single().ScheduledTime);
    }

    [Fact]
    public void Build_OrdersItemsByCuratedOrderIndexAndRenumbersFromZero()
    {
        var first = Place(5, 1m);
        var second = Place(9, 2m);
        var source = Source(second, first);

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, PlannedStart, TravelMode.Auto);

        var items = trip.Items.OrderBy(item => item.OrderIndex).ToList();
        Assert.Equal(first.PlaceId, items[0].PlaceId);
        Assert.Equal(second.PlaceId, items[1].PlaceId);
        Assert.Equal([0, 1], items.Select(item => item.OrderIndex));
    }

    [Fact]
    public void Build_DurationHoursIsTotalMinutesRoundedUp()
    {
        // 60 + 1 + 75 + 1 + 90 = 227 phút → 4 giờ.
        var source = Source(
            Place(0, 1m, PlaceCategory.Cafe), Place(1, 1m, PlaceCategory.Food), Place(2, 1m, PlaceCategory.Culture));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, PlannedStart, TravelMode.Auto);

        Assert.Equal(4, trip.DurationHours);
    }

    [Fact]
    public void Build_SingleShortStop_HasAtLeastOneHour()
    {
        var source = Source(Place(0, 1m, PlaceCategory.CheckIn));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, PlannedStart, TravelMode.Auto);

        Assert.Equal(1, trip.DurationHours);
    }

    [Fact]
    public void Build_CopiesBudgetRangePlannedStartAndTravelMode()
    {
        var source = Source(costMin: 100_000m, costMax: 300_000m, Place(0, 1m));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, PlannedStart, TravelMode.Walking);

        Assert.Equal(100_000m, trip.BudgetMin);
        Assert.Equal(300_000m, trip.BudgetMax);
        Assert.Equal(PlannedStart, trip.PlannedStartAt);
        Assert.Equal(TravelMode.Walking, trip.TravelMode);
        Assert.InRange(trip.DurationHours, 1, 24);
    }

    [Fact]
    public void Build_NoPlaces_Throws()
    {
        var source = Source();

        Assert.Throws<ArgumentException>(() =>
            CuratedTripBuilder.Build(UserId, source, null, null, PlannedStart, TravelMode.Auto));
    }

    [Fact]
    public void TotalMinutes_SumsVisitsAndTravel_AndIncludesTravelFromOrigin()
    {
        var source = Source(
            Place(0, 1m, PlaceCategory.Cafe, latitude: 10.770),
            Place(1, 1m, PlaceCategory.Food, latitude: 10.770),
            Place(2, 1m, PlaceCategory.Culture, latitude: 10.770));

        var withoutOrigin = CuratedTripBuilder.TotalMinutes(source, TravelMode.Auto, null);
        var withOrigin = CuratedTripBuilder.TotalMinutes(source, TravelMode.Auto, new ScheduleOrigin(10.750, 106.70));

        Assert.Equal(227, withoutOrigin);
        Assert.Equal(235, withOrigin); // + 8 phút đi tới chặng đầu
    }

    private static CuratedPlaceForApplyReadModel Place(
        int order,
        decimal costMax,
        PlaceCategory category = PlaceCategory.Cafe,
        double latitude = 10.77,
        double longitude = 106.70) =>
        new(Guid.NewGuid(), order, latitude, longitude, costMax, category);

    private static CuratedItineraryForApplyReadModel Source(params CuratedPlaceForApplyReadModel[] places) =>
        Source(0m, 100_000m, places);

    private static CuratedItineraryForApplyReadModel Source(
        decimal costMin,
        decimal costMax,
        params CuratedPlaceForApplyReadModel[] places) =>
        new(Guid.NewGuid(), "Lịch trình mẫu thử", 240, costMin, costMax, places);
}
