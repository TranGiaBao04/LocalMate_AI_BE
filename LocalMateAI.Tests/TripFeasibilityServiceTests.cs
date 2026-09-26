using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Trips;

namespace LocalMateAI.Tests;

public sealed class TripFeasibilityServiceTests
{
    // 2026-09-26 08:00 UTC = 15:00 giờ Việt Nam.
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));
    private static readonly Guid AnchorStationId = Guid.NewGuid();
    private static readonly Guid NeighbourStationId = Guid.NewGuid();

    [Fact]
    public async Task Check_CountsCandidatesFromAnchorAndNeighbourStations()
    {
        var fixture = new Fixture([Place("A", AnchorStationId, 100), Place("B", NeighbourStationId, 200)]);

        var result = await fixture.Service.CheckFeasibilityAsync(Request());

        var response = result.Response!;
        Assert.True(response.IsFeasible);
        Assert.Equal(2, response.CandidatePlaceCount);
        Assert.Equal(AnchorStationId, fixture.Matching.RequestedStationId);
        Assert.Equal(MetroClusterMatchingService.CandidateRadiusMeters, fixture.Matching.RequestedRadius);
    }

    [Fact]
    public async Task Check_AllCandidatesOverBudget_IsNotFeasibleButStillCountsCandidates()
    {
        var fixture = new Fixture([Place("A", AnchorStationId, 100, cost: 900_000m)]);

        var response = (await fixture.Service.CheckFeasibilityAsync(Request(budgetMax: 300_000m))).Response!;

        Assert.False(response.IsFeasible);
        Assert.Equal("InsufficientCandidates", response.Reason);
        Assert.Equal(1, response.CandidatePlaceCount);
    }

    [Fact]
    public async Task Check_OnlyCandidateLongerThanDuration_IsNotFeasible()
    {
        // Một địa điểm Culture (90 phút) mà chỉ rảnh 1 giờ.
        var fixture = new Fixture([Place("A", AnchorStationId, 100, category: "Culture")]);

        var response = (await fixture.Service.CheckFeasibilityAsync(Request(durationHours: 1))).Response!;

        Assert.False(response.IsFeasible);
        Assert.Equal(0, response.EstimatedStopCount);
    }

    [Fact]
    public async Task Check_OutsideServiceArea_DoesNotLoadCandidates()
    {
        var fixture = new Fixture([Place("A", AnchorStationId, 100)], withinServiceArea: false);

        var response = (await fixture.Service.CheckFeasibilityAsync(Request())).Response!;

        Assert.False(response.IsFeasible);
        Assert.Equal("OutOfServiceArea", response.Reason);
        Assert.Equal(0, fixture.Matching.Calls);
    }

    [Fact]
    public async Task Check_PastPlannedDate_FailsValidationOnPlannedDateWithoutLoadingCandidates()
    {
        var fixture = new Fixture([Place("A", AnchorStationId, 100)]);

        var result = await fixture.Service.CheckFeasibilityAsync(Request(plannedDate: new DateOnly(2026, 9, 25)));

        Assert.Equal(TripFeasibilityResultStatus.ValidationFailed, result.Status);
        Assert.Contains("PlannedDate", result.ValidationErrors!.Keys);
        Assert.Equal(0, fixture.Matching.Calls);
    }

    [Fact]
    public async Task Check_SkipsCandidateThatDoesNotFit_AndStillFindsOneThatDoes()
    {
        // Rảnh 1 giờ: Culture 90' không vừa nhưng Check-in 45' vừa → vẫn khả thi với đúng 1 chặng.
        var fixture = new Fixture(
        [
            Place("Xa", AnchorStationId, 700, category: "Culture"),
            Place("Gần", AnchorStationId, 50, category: "CheckIn")
        ]);

        var response = (await fixture.Service.CheckFeasibilityAsync(Request(durationHours: 1))).Response!;

        Assert.True(response.IsFeasible);
        Assert.Equal(1, response.EstimatedStopCount);
    }

    private static TripRequestDto Request(
        int durationHours = 4, decimal budgetMax = 500_000m, DateOnly? plannedDate = null) =>
        new(10.7721, 106.6980, durationHours, 0m, budgetMax, [], PlannedDate: plannedDate);

    private static PlaceCandidateDto Place(
        string name, Guid stationId, double distance, string category = "Cafe", decimal cost = 50_000m) =>
        new(Guid.NewGuid(), name, "Địa chỉ", 10.7721, 106.6980, category, 0m, cost, null,
            stationId, "Bến Thành", 1, distance);

    private sealed class Fixture
    {
        public Fixture(IReadOnlyList<PlaceCandidateDto> candidates, bool withinServiceArea = true)
        {
            Matching = new FakeMatching(candidates);
            Service = new TripFeasibilityService(
                new TripRequestValidator(Clock),
                new FakeOrigin(withinServiceArea),
                new TripCriteriaNormalizationService(),
                Matching,
                new CandidateFilterService());
        }

        public TripFeasibilityService Service { get; }
        public FakeMatching Matching { get; }
    }

    private sealed class FakeMatching(IReadOnlyList<PlaceCandidateDto> candidates) : IMetroClusterMatchingService
    {
        public int Calls { get; private set; }
        public Guid? RequestedStationId { get; private set; }
        public double? RequestedRadius { get; private set; }

        public Task<IReadOnlyList<PlaceCandidateDto>> GetCandidatesAsync(
            Guid originStationId, double radiusMeters, CancellationToken cancellationToken = default)
        {
            Calls++;
            RequestedStationId = originStationId;
            RequestedRadius = radiusMeters;
            return Task.FromResult(candidates);
        }
    }

    private sealed class FakeOrigin(bool withinServiceArea) : ITripOriginResolverService
    {
        public Task<TripOriginResolution?> ResolveAsync(
            TripRequestDto request, CancellationToken cancellationToken = default) =>
            ResolveAsync(request.StartLatitude, request.StartLongitude, cancellationToken);

        public Task<TripOriginResolution?> ResolveAsync(
            double latitude, double longitude, CancellationToken cancellationToken = default) =>
            Task.FromResult<TripOriginResolution?>(new TripOriginResolution(
                new NearestStationResult(AnchorStationId, "Bến Thành", 10.7721, 106.6980, withinServiceArea ? 300 : 50_000),
                withinServiceArea));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
