using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class CandidateFilterServiceTests
{
    private readonly CandidateFilterService _sut = new();

    [Fact]
    public void Filter_ExpensivePlace_BeyondBudgetPerStop_IsExcludedWithBudgetReason()
    {
        // Arrange
        var criteria = Criteria(budgetPerStop: 100_000m);
        var candidate = MakeCandidate("Quán đắt", costMax: 150_000m);

        // Act
        var result = _sut.Filter([candidate], criteria);

        // Assert
        Assert.Empty(result.Passed);
        var excluded = Assert.Single(result.Excluded);
        Assert.Equal("budget", excluded.Reason);
    }

    [Fact]
    public void Filter_PlaceCostExactlyBudgetPerStop_Passes()
    {
        // Arrange
        var criteria = Criteria(budgetPerStop: 100_000m);
        var candidate = MakeCandidate("Quán vừa khít", costMax: 100_000m);

        // Act
        var result = _sut.Filter([candidate], criteria);

        // Assert
        Assert.Single(result.Passed);
        Assert.Empty(result.Excluded);
    }

    [Fact]
    public void Filter_FreePlace_PassesEvenWithZeroBudget()
    {
        // Arrange
        var criteria = Criteria(budgetPerStop: 0m, stopCount: 1);
        var candidate = MakeCandidate("Công viên miễn phí", costMax: 0m);

        // Act
        var result = _sut.Filter([candidate], criteria);

        // Assert
        Assert.Single(result.Passed);
        Assert.Empty(result.Excluded);
    }

    [Fact]
    public void Filter_KeepsAllAffordablePlaces_DoesNotTruncateByStopCount()
    {
        // Arrange — việc lấy top N do orchestrator (TripMatchingService) đảm nhiệm, filter không được cắt danh sách.
        var criteria = Criteria(budgetPerStop: 100_000m, stopCount: 2);
        var candidates = Enumerable.Range(1, 5)
            .Select(i => MakeCandidate($"Địa điểm {i}", costMax: 50_000m))
            .ToList();

        // Act
        var result = _sut.Filter(candidates, criteria);

        // Assert
        Assert.Equal(5, result.Passed.Count);
        Assert.Empty(result.Excluded);
    }

    [Fact]
    public void Filter_MixedList_OnlyBudgetViolationsAreExcluded()
    {
        // Arrange
        var criteria = Criteria(budgetPerStop: 100_000m);
        var cheap = MakeCandidate("Rẻ", costMax: 30_000m);
        var expensive = MakeCandidate("Đắt", costMax: 500_000m);

        // Act
        var result = _sut.Filter([cheap, expensive], criteria);

        // Assert
        var passed = Assert.Single(result.Passed);
        Assert.Equal("Rẻ", passed.PlaceName);
        var excluded = Assert.Single(result.Excluded);
        Assert.Equal("Đắt", excluded.PlaceName);
        Assert.Equal("budget", excluded.Reason);
    }

    private static NormalizedTripCriteria Criteria(decimal budgetPerStop, int stopCount = 3) =>
        new(TripDurationCategory.HalfDay, stopCount, BudgetTier.Standard, budgetPerStop);

    private static PlaceCandidateDto MakeCandidate(string name, decimal costMax) =>
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
            DistanceFromStationMeters: 100);
}