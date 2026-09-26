using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class HeuristicFallbackEngineTests
{
    [Fact]
    public async Task GenerateFallback_ValidationFailed_PropagatesErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["DurationHours"] = ["'Duration Hours' must be between 1 and 24."]
        };
        var engine = new HeuristicFallbackEngine(
            new FakeTripMatchingService(
                new TripMatchingResult(
                    TripMatchingResultStatus.ValidationFailed,
                    ValidationErrors: errors)));

        var result = await engine.GenerateFallbackAsync(Request(), "llm_timeout");

        Assert.Equal(FallbackItineraryStatus.ValidationFailed, result.Status);
        Assert.Null(result.Payload);
        Assert.Same(errors, result.ValidationErrors);
    }

    [Fact]
    public async Task GenerateFallback_SufficientMatch_BuildsStopsAndPassesReason()
    {
        var candidate = MakeCandidate("Cà phê Bến Thành");
        var match = new TripMatchingResponse(
            IsSufficient: true,
            InsufficiencyReason: null,
            StationName: "Bến Thành",
            EstimatedStopCount: 2,
            BudgetTier: "Standard",
            Candidates:
            [
                new ScoredPlaceDto(candidate, 1.0, [])
            ],
            Excluded: []);
        var engine = new HeuristicFallbackEngine(
            new FakeTripMatchingService(
                new TripMatchingResult(TripMatchingResultStatus.Success, match)));

        var result = await engine.GenerateFallbackAsync(Request(), "llm_timeout");

        Assert.Equal(FallbackItineraryStatus.Success, result.Status);
        var payload = result.Payload!;
        Assert.Equal("llm_timeout", payload.FallbackReason);
        Assert.True(payload.IsSufficient);
        Assert.Equal("Bến Thành", payload.StationName);
        Assert.Equal(2, payload.EstimatedStopCount);
        Assert.Equal("Standard", payload.BudgetTier);
        var stop = Assert.Single(payload.Stops);
        Assert.Equal(candidate.PlaceId, stop.PlaceId);
        Assert.Equal(new TimeOnly(8, 1), stop.ScheduledTime); // 08:00 rời đi + 1 phút (tối thiểu) tới địa điểm ngay tại điểm xuất phát
        Assert.Contains("Bến Thành", stop.Reasoning); // không tag trùng nên chỉ nhắc ga và khoảng cách
    }

    [Fact]
    public async Task GenerateFallback_InsufficientMatch_ReturnsEmptyStopsWithReason()
    {
        var match = new TripMatchingResponse(
            IsSufficient: false,
            InsufficiencyReason: "OutOfServiceArea",
            StationName: "Công viên Văn Thánh",
            EstimatedStopCount: 3,
            BudgetTier: "Economy",
            Candidates: [],
            Excluded: []);
        var engine = new HeuristicFallbackEngine(
            new FakeTripMatchingService(
                new TripMatchingResult(TripMatchingResultStatus.Success, match)));

        var result = await engine.GenerateFallbackAsync(Request(), "heuristic");

        Assert.Equal(FallbackItineraryStatus.Success, result.Status);
        var payload = result.Payload!;
        Assert.False(payload.IsSufficient);
        Assert.Equal("OutOfServiceArea", payload.InsufficiencyReason);
        Assert.Empty(payload.Stops);
    }

    private static TripRequestDto Request() =>
        new(
            StartLatitude: 10.77,
            StartLongitude: 106.69,
            DurationHours: 3,
            BudgetMin: 0,
            BudgetMax: 300_000m,
            TagIds: []);

    private static PlaceCandidateDto MakeCandidate(string name) =>
        new(
            PlaceId: Guid.NewGuid(),
            PlaceName: name,
            Address: "Địa chỉ",
            Latitude: 10.77,
            Longitude: 106.69,
            Category: "Cafe",
            EstimatedCostMin: 0,
            EstimatedCostMax: 50_000m,
            ImageUrl: null,
            StationId: Guid.NewGuid(),
            StationName: "Bến Thành",
            StationOrder: 1,
            DistanceFromStationMeters: 100);

    private sealed class FakeTripMatchingService(TripMatchingResult response) : ITripMatchingService
    {
        public Task<TripMatchingResult> MatchAsync(
            TripRequestDto request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(response);
    }
}