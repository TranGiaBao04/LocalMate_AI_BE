using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class CuratedTripBuilderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Build_DefaultsStartTimeAndDividesDurationAcrossStops()
    {
        var source = Source(durationMinutes: 240, Place(0, 50_000m), Place(1, 30_000m), Place(2, 0m));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, null);

        Assert.Equal(TripStatus.Draft, trip.Status);
        Assert.Equal(UserId, trip.UserId);
        Assert.Equal(3, trip.Items.Count);
        var items = trip.Items.OrderBy(item => item.OrderIndex).ToList();
        Assert.Equal([0, 1, 2], items.Select(item => item.OrderIndex));
        Assert.All(items, item => Assert.Equal(80, item.EstimatedDurationMinutes)); // 240 / 3
        Assert.Equal(new TimeOnly(8, 0), items[0].ScheduledTime);
        Assert.Equal(new TimeOnly(9, 20), items[1].ScheduledTime);
        Assert.Equal(new TimeOnly(10, 40), items[2].ScheduledTime);
        Assert.Equal([50_000m, 30_000m, 0m], items.Select(item => item.EstimatedBudget));
        Assert.All(items, item => Assert.Equal(trip.Id, item.TripId));
        Assert.All(items, item => Assert.Contains("Lịch trình mẫu", item.Reasoning));
    }

    [Fact]
    public void Build_UsesStartTimeAndLocationWhenProvided()
    {
        var source = Source(durationMinutes: 120, Place(0, 10_000m), Place(1, 10_000m));

        var trip = CuratedTripBuilder.Build(UserId, source, 10.80, 106.65, new TimeOnly(13, 30));

        Assert.Equal(10.80, trip.StartLatitude);
        Assert.Equal(106.65, trip.StartLongitude);
        Assert.Equal(new TimeOnly(13, 30), trip.Items.OrderBy(item => item.OrderIndex).First().ScheduledTime);
    }

    [Fact]
    public void Build_WithoutStartLocation_UsesFirstPlaceCoordinates()
    {
        var source = Source(durationMinutes: 120, Place(1, 10_000m, 10.90, 106.90), Place(0, 10_000m, 10.77, 106.70));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, null);

        Assert.Equal(10.77, trip.StartLatitude);
        Assert.Equal(106.70, trip.StartLongitude);
    }

    [Fact]
    public void Build_OrdersItemsByCuratedOrderIndexAndRenumbersFromZero()
    {
        var first = Place(5, 1m);
        var second = Place(9, 2m);
        var source = Source(durationMinutes: 120, second, first);

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, null);

        var items = trip.Items.OrderBy(item => item.OrderIndex).ToList();
        Assert.Equal(first.PlaceId, items[0].PlaceId);
        Assert.Equal(second.PlaceId, items[1].PlaceId);
        Assert.Equal([0, 1], items.Select(item => item.OrderIndex));
    }

    [Fact]
    public void Build_ZeroDuration_FallsBackToDefaultMinutesPerStop()
    {
        var source = Source(durationMinutes: 0, Place(0, 1m), Place(1, 1m));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, null);

        Assert.All(trip.Items, item => Assert.Equal(CuratedTripBuilder.DefaultMinutesPerStop, item.EstimatedDurationMinutes));
        Assert.Equal(3, trip.DurationHours); // 2 x 90 phút = 180 phút
    }

    [Fact]
    public void Build_VeryShortDuration_IsRaisedToMinimumMinutesPerStop()
    {
        var source = Source(durationMinutes: 30, Place(0, 1m), Place(1, 1m), Place(2, 1m));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, null);

        Assert.All(trip.Items, item => Assert.Equal(CuratedTripBuilder.MinMinutesPerStop, item.EstimatedDurationMinutes));
        Assert.Equal(2, trip.DurationHours); // 3 x 30 phút = 90 phút → làm tròn lên 2 giờ
    }

    [Fact]
    public void Build_CopiesBudgetRangeAndKeepsDurationHoursWithinBounds()
    {
        var source = Source(durationMinutes: 240, costMin: 100_000m, costMax: 300_000m, Place(0, 1m));

        var trip = CuratedTripBuilder.Build(UserId, source, null, null, null);

        Assert.Equal(100_000m, trip.BudgetMin);
        Assert.Equal(300_000m, trip.BudgetMax);
        Assert.InRange(trip.DurationHours, 1, 24);
    }

    [Fact]
    public void Build_NoPlaces_Throws()
    {
        var source = Source(durationMinutes: 120);

        Assert.Throws<ArgumentException>(() => CuratedTripBuilder.Build(UserId, source, null, null, null));
    }

    private static CuratedPlaceForApplyReadModel Place(
        int order,
        decimal costMax,
        double latitude = 10.77,
        double longitude = 106.70) =>
        new(Guid.NewGuid(), order, latitude, longitude, costMax);

    private static CuratedItineraryForApplyReadModel Source(
        int durationMinutes,
        params CuratedPlaceForApplyReadModel[] places) =>
        Source(durationMinutes, 0m, 100_000m, places);

    private static CuratedItineraryForApplyReadModel Source(
        int durationMinutes,
        decimal costMin,
        decimal costMax,
        params CuratedPlaceForApplyReadModel[] places) =>
        new(Guid.NewGuid(), "Lịch trình mẫu thử", durationMinutes, costMin, costMax, places);
}
