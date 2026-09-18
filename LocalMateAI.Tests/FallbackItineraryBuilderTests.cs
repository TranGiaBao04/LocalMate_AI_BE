using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class FallbackItineraryBuilderTests
{
    [Fact]
    public void BuildStops_EmptyCandidates_ReturnsEmpty()
    {
        var stops = FallbackItineraryBuilder.BuildStops([]);

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

        var stops = FallbackItineraryBuilder.BuildStops(candidates);

        Assert.Equal(3, stops.Count);
        Assert.Equal(0, stops[0].OrderIndex);
        Assert.Equal(1, stops[1].OrderIndex);
        Assert.Equal(2, stops[2].OrderIndex);

        Assert.Equal(new TimeOnly(8, 0), stops[0].ScheduledTime);
        Assert.Equal(new TimeOnly(9, 30), stops[1].ScheduledTime);
        Assert.Equal(new TimeOnly(11, 0), stops[2].ScheduledTime);

        Assert.All(stops, stop => Assert.Equal(90, stop.EstimatedDurationMinutes));
    }

    [Fact]
    public void BuildStops_UsesCandidateCostMaxAsBudget()
    {
        var stops = FallbackItineraryBuilder.BuildStops(
            [Scored(MakeCandidate("Quán ăn", costMax: 150_000m))]);

        Assert.Equal(150_000m, stops[0].EstimatedBudget);
    }

    [Fact]
    public void BuildStops_ReasoningContainsScoreStationAndDistance()
    {
        var stops = FallbackItineraryBuilder.BuildStops(
            [Scored(MakeCandidate("Cà phê Bến Thành", distance: 250.0))]);

        var reasoning = stops[0].Reasoning;

        Assert.Contains("Fallback heuristic", reasoning);
        Assert.Contains("Bến Thành", reasoning);
        Assert.Contains("250m", reasoning);
    }

    [Fact]
    public void BuildStops_ReasoningFormatsMatchScoreAsPercent()
    {
        var stops = FallbackItineraryBuilder.BuildStops(
            [Scored(MakeCandidate("Điểm hợp sở thích"), score: 1.0)]);

        Assert.Contains("100%", stops[0].Reasoning);
    }

    private static ScoredPlaceDto Scored(PlaceCandidateDto candidate, double score = 1.0) =>
        new(candidate, score, []);

    private static PlaceCandidateDto MakeCandidate(string name, decimal costMax = 50_000m, double distance = 100.0) =>
        new(
            PlaceId: Guid.NewGuid(),
            PlaceName: name,
            Address: "Địa chỉ",
            Latitude: 10.77,
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