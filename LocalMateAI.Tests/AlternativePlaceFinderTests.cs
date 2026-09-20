using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class AlternativePlaceFinderTests
{
    private static readonly Guid StationA = Guid.NewGuid();
    private static readonly Guid StationB = Guid.NewGuid();

    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> NoTags =
        new Dictionary<Guid, IReadOnlyList<Guid>>();

    private readonly AlternativePlaceFinder _sut = new(new TagSimilarityScorer());

    [Fact]
    public void Find_ReturnsOnlyCandidatesFromSameStation()
    {
        var current = MakeCandidate(costMax: 100);
        var sameStation = MakeCandidate(costMax: 100);
        var otherStation = MakeCandidate(costMax: 100, stationId: StationB);

        var result = _sut.Find(current, [sameStation, otherStation], NoTags, [], limit: 5);

        Assert.Equal(sameStation.PlaceId, Assert.Single(result).Candidate.PlaceId);
    }

    [Fact]
    public void Find_ExcludesCurrentPlaceAndPlacesAlreadyInTrip()
    {
        var current = MakeCandidate(costMax: 100);
        var inTrip = MakeCandidate(costMax: 100);
        var available = MakeCandidate(costMax: 100);

        var result = _sut.Find(
            current, [current, inTrip, available], NoTags, [inTrip.PlaceId], limit: 5);

        Assert.Equal(available.PlaceId, Assert.Single(result).Candidate.PlaceId);
    }

    [Fact]
    public void Find_CostAboveTolerance_IsExcluded()
    {
        var current = MakeCandidate(costMax: 100);
        var cheaper = MakeCandidate(costMax: 80);
        var atLimit = MakeCandidate(costMax: 125);
        var tooExpensive = MakeCandidate(costMax: 126);

        var ids = _sut
            .Find(current, [cheaper, atLimit, tooExpensive], NoTags, [], limit: 5)
            .Select(scored => scored.Candidate.PlaceId)
            .ToHashSet();

        Assert.Contains(cheaper.PlaceId, ids);
        Assert.Contains(atLimit.PlaceId, ids);
        Assert.DoesNotContain(tooExpensive.PlaceId, ids);
    }

    [Fact]
    public void Find_FreeCurrentPlace_ReturnsOnlyFreeCandidates()
    {
        var current = MakeCandidate(costMax: 0);
        var free = MakeCandidate(costMax: 0);
        var paid = MakeCandidate(costMax: 10);

        var result = _sut.Find(current, [free, paid], NoTags, [], limit: 5);

        Assert.Equal(free.PlaceId, Assert.Single(result).Candidate.PlaceId);
    }

    [Fact]
    public void Find_RanksByTagMatchDescending()
    {
        var tagA = Guid.NewGuid();
        var tagB = Guid.NewGuid();
        var current = MakeCandidate(costMax: 100);
        var full = MakeCandidate(costMax: 100);
        var half = MakeCandidate(costMax: 100);
        var none = MakeCandidate(costMax: 100);
        var tags = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [current.PlaceId] = new List<Guid> { tagA, tagB },
            [full.PlaceId] = new List<Guid> { tagA, tagB },
            [half.PlaceId] = new List<Guid> { tagA }
        };

        var result = _sut.Find(current, [none, half, full], tags, [], limit: 5);

        Assert.Equal(
            new[] { full.PlaceId, half.PlaceId, none.PlaceId },
            result.Select(scored => scored.Candidate.PlaceId).ToArray());
    }

    [Fact]
    public void Find_SameScore_PrefersSameCategoryOverCloserCost()
    {
        var tag = Guid.NewGuid();
        var current = MakeCandidate(costMax: 100, category: "Food");
        var sameCategory = MakeCandidate(costMax: 120, category: "Food");
        var otherCategory = MakeCandidate(costMax: 100, category: "Cafe");
        var tags = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [current.PlaceId] = new List<Guid> { tag },
            [sameCategory.PlaceId] = new List<Guid> { tag },
            [otherCategory.PlaceId] = new List<Guid> { tag }
        };

        var result = _sut.Find(current, [otherCategory, sameCategory], tags, [], limit: 5);

        Assert.Equal(sameCategory.PlaceId, result[0].Candidate.PlaceId);
    }

    [Fact]
    public void Find_FullTie_PrefersCloserToStation()
    {
        var current = MakeCandidate(costMax: 100);
        var near = MakeCandidate(costMax: 100, distance: 100);
        var far = MakeCandidate(costMax: 100, distance: 300);

        var result = _sut.Find(current, [far, near], NoTags, [], limit: 5);

        Assert.Equal(near.PlaceId, result[0].Candidate.PlaceId);
    }

    [Fact]
    public void Find_RespectsLimit()
    {
        var current = MakeCandidate(costMax: 100);
        var candidates = new[]
        {
            MakeCandidate(costMax: 100),
            MakeCandidate(costMax: 100),
            MakeCandidate(costMax: 100)
        };

        var result = _sut.Find(current, candidates, NoTags, [], limit: 2);

        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Find_NonPositiveLimit_ReturnsEmpty(int limit)
    {
        var current = MakeCandidate(costMax: 100);
        var candidate = MakeCandidate(costMax: 100);

        Assert.Empty(_sut.Find(current, [candidate], NoTags, [], limit));
    }

    [Fact]
    public void Find_NoEligibleCandidates_ReturnsEmpty()
    {
        var current = MakeCandidate(costMax: 100);
        var otherStation = MakeCandidate(costMax: 100, stationId: StationB);

        Assert.Empty(_sut.Find(current, [otherStation], NoTags, [], limit: 5));
    }

    [Theory]
    [InlineData(100, 125, true)]
    [InlineData(100, 126, false)]
    [InlineData(100, 0, true)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    public void IsWithinCostTolerance_HandlesBoundaries(
        decimal currentCostMax,
        decimal candidateCostMax,
        bool expected)
    {
        Assert.Equal(
            expected,
            AlternativePlaceFinder.IsWithinCostTolerance(currentCostMax, candidateCostMax));
    }

    private static PlaceCandidateDto MakeCandidate(
        decimal costMax,
        Guid? stationId = null,
        string category = "Food",
        double distance = 100) =>
        new(
            Guid.NewGuid(),
            "Place",
            "Address",
            10.77,
            106.69,
            category,
            0,
            costMax,
            null,
            stationId ?? StationA,
            "Bến Thành",
            1,
            distance);
}
