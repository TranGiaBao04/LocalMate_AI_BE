using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class FallbackItineraryBuilderTests
{
    [Fact]
    public void BuildStops_EmptyCandidates_ReturnsEmpty()
    {
        var stops = FallbackItineraryBuilder.BuildStops([], durationHours: 8);

        Assert.Empty(stops);
    }

    [Fact]
    public void BuildStops_AssignsCumulativeScheduleFromEightAM()
    {
        var candidates = new[]
        {
            Scored(MakeCandidate("Điểm A")),
            Scored(MakeCandidate("Điểm B")),
            Scored(MakeCandidate("Điểm C"))
        };

        var stops = FallbackItineraryBuilder.BuildStops(candidates, durationHours: 8);

        Assert.Equal(3, stops.Count);
        Assert.Equal(0, stops[0].OrderIndex);
        Assert.Equal(1, stops[1].OrderIndex);
        Assert.Equal(2, stops[2].OrderIndex);

        // Cùng vị trí: mỗi chặng 60' (Cafe) + 1' di chuyển tối thiểu
        Assert.Equal(new TimeOnly(8, 0), stops[0].ScheduledTime);
        Assert.Equal(new TimeOnly(9, 1), stops[1].ScheduledTime);
        Assert.Equal(new TimeOnly(10, 2), stops[2].ScheduledTime);

        Assert.All(stops, stop => Assert.Equal(60, stop.EstimatedDurationMinutes));
    }

    [Fact]
    public void BuildStops_KeepsTopCandidateFirstAndOrdersRestByNearest()
    {
        var top = Scored(MakeCandidate("A (điểm cao nhất)", latitude: 10.770));
        var far = Scored(MakeCandidate("B (xa)", latitude: 10.790));
        var near = Scored(MakeCandidate("C (sát A)", latitude: 10.771));

        var stops = FallbackItineraryBuilder.BuildStops([top, far, near], durationHours: 10);

        Assert.Equal(
            ["A (điểm cao nhất)", "C (sát A)", "B (xa)"],
            stops.Select(stop => stop.PlaceName));
        Assert.Equal([0, 1, 2], stops.Select(stop => stop.OrderIndex));
    }

    [Fact]
    public void BuildStops_DropsTrailingStopsThatOverflowDuration()
    {
        var candidates = new[] { Scored(MakeCandidate("A")), Scored(MakeCandidate("B")), Scored(MakeCandidate("C")) };

        // Kết thúc ở phút 60, 121, 182 → chỉ 2 chặng đầu nằm trong 3 giờ
        var stops = FallbackItineraryBuilder.BuildStops(candidates, durationHours: 3);

        Assert.Equal(2, stops.Count);
    }

    [Fact]
    public void BuildStops_UsesCandidateCostMaxAsBudget()
    {
        var stops = FallbackItineraryBuilder.BuildStops(
            [Scored(MakeCandidate("Quán ăn", costMax: 150_000m))], durationHours: 8);

        Assert.Equal(150_000m, stops[0].EstimatedBudget);
    }

    [Fact]
    public void BuildStops_ReasoningContainsScoreStationAndDistance()
    {
        var stops = FallbackItineraryBuilder.BuildStops(
            [Scored(MakeCandidate("Cà phê Bến Thành", distance: 250.0))], durationHours: 8);

        var reasoning = stops[0].Reasoning;

        Assert.Contains("Fallback heuristic", reasoning);
        Assert.Contains("Bến Thành", reasoning);
        Assert.Contains("250m", reasoning);
    }

    [Fact]
    public void BuildStops_ReasoningFormatsMatchScoreAsPercent()
    {
        var stops = FallbackItineraryBuilder.BuildStops(
            [Scored(MakeCandidate("Điểm hợp sở thích"), score: 1.0)], durationHours: 8);

        Assert.Contains("100%", stops[0].Reasoning);
    }

    private static ScoredPlaceDto Scored(PlaceCandidateDto candidate, double score = 1.0) =>
        new(candidate, score, []);

    private static PlaceCandidateDto MakeCandidate(
        string name, decimal costMax = 50_000m, double distance = 100.0, double latitude = 10.77) =>
        new(
            PlaceId: Guid.NewGuid(),
            PlaceName: name,
            Address: "Địa chỉ",
            Latitude: latitude,
            Longitude: 106.69,
            Category: "Cafe",
            EstimatedCostMin: 0,
            EstimatedCostMax: costMax,
            ImageUrl: null,
            StationId: Guid.NewGuid(),
            StationName: "Bến Thành",
            StationOrder: 1,
            DistanceFromStationMeters: distance);
}