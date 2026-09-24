using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class GeneratedTripBuilderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Build_CopiesRequestIntoDraftTrip()
    {
        var trip = GeneratedTripBuilder.Build(UserId, Request(), [Stop(0, 8, 0)], []);

        Assert.Equal(UserId, trip.UserId);
        Assert.Equal(TripStatus.Draft, trip.Status);
        Assert.Equal(10.77, trip.StartLatitude);
        Assert.Equal(106.69, trip.StartLongitude);
        Assert.Equal(6, trip.DurationHours);
        Assert.Equal(50_000m, trip.BudgetMin);
        Assert.Equal(900_000m, trip.BudgetMax);
        Assert.Equal(TravelMode.Motorbike, trip.TravelMode);
    }

    [Fact]
    public void Build_MapsEachStopToItineraryItem()
    {
        var first = Stop(0, 8, 0, minutes: 60, budget: 100_000m, reasoning: "Lý do A");
        var second = Stop(1, 9, 5, minutes: 45, budget: 0m, reasoning: "Lý do B");

        var trip = GeneratedTripBuilder.Build(UserId, Request(), [first, second], []);

        var items = trip.Items.OrderBy(item => item.OrderIndex).ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(trip.Id, item.TripId));
        Assert.Equal(first.PlaceId, items[0].PlaceId);
        Assert.Equal(new TimeOnly(8, 0), items[0].ScheduledTime);
        Assert.Equal(60, items[0].EstimatedDurationMinutes);
        Assert.Equal(100_000m, items[0].EstimatedBudget);
        Assert.Equal("Lý do A", items[0].Reasoning);
        Assert.Equal(new TimeOnly(9, 5), items[1].ScheduledTime);
        Assert.Equal(0m, items[1].EstimatedBudget);
        Assert.False(items[1].IsVisited);
    }

    [Fact]
    public void Build_CreatesTripTagsForEachTagId()
    {
        var tagA = Guid.NewGuid();
        var tagB = Guid.NewGuid();

        var trip = GeneratedTripBuilder.Build(UserId, Request(), [Stop(0, 8, 0)], [tagA, tagB]);

        Assert.Equal([tagA, tagB], trip.Tags.Select(tag => tag.TagId));
        Assert.All(trip.Tags, tag => Assert.Equal(trip.Id, tag.TripId));
    }

    [Fact]
    public void Build_NoStops_Throws()
    {
        Assert.Throws<ArgumentException>(() => GeneratedTripBuilder.Build(UserId, Request(), [], []));
    }

    private static TripRequestDto Request() =>
        new(10.77, 106.69, 6, 50_000m, 900_000m, [], TravelMode.Motorbike);

    private static FallbackStopDto Stop(
        int order, int hour, int minute, int minutes = 60, decimal budget = 50_000m, string reasoning = "lý do") =>
        new(
            Guid.NewGuid(), $"Place {order}", "Địa chỉ", 10.77, 106.69, "Cafe",
            order, new TimeOnly(hour, minute), minutes, budget, reasoning, 1.0, 100, "Bến Thành");
}
