using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TagSimilarityScorerTests
{
    private readonly TagSimilarityScorer _sut = new();

    [Fact]
    public void Score_PartialTagOverlap_ReturnsCoverageRatio()
    {
        // Arrange — trip muốn 2 tag, place chỉ có 1 → coverage 1/2 = 0.5
        var tripTags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var placeTag = tripTags[0];
        var candidate = MakeCandidate();
        var placeTags = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [candidate.PlaceId] = new List<Guid> { placeTag }
        };

        // Act
        var scored = Assert.Single(_sut.Score([candidate], placeTags, tripTags));

        // Assert
        Assert.Equal(0.5, scored.MatchScore, precision: 3);
        Assert.Equal([placeTag], scored.MatchedTagIds);
    }

    [Fact]
    public void Score_FullTagOverlap_ReturnsOne()
    {
        var tripTags = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var candidate = MakeCandidate();
        var placeTags = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [candidate.PlaceId] = tripTags
        };

        var scored = Assert.Single(_sut.Score([candidate], placeTags, tripTags));

        Assert.Equal(1.0, scored.MatchScore);
    }

    [Fact]
    public void Score_NoTagOverlap_ReturnsZero()
    {
        var tripTags = new List<Guid> { Guid.NewGuid() };
        var candidate = MakeCandidate();
        var placeTags = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [candidate.PlaceId] = new List<Guid> { Guid.NewGuid() }
        };

        var scored = Assert.Single(_sut.Score([candidate], placeTags, tripTags));

        Assert.Equal(0.0, scored.MatchScore);
        Assert.Empty(scored.MatchedTagIds);
    }

    [Fact]
    public void Score_EmptyTripTags_ReturnsNeutralScore()
    {
        var candidate = MakeCandidate();
        var placeTags = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [candidate.PlaceId] = new List<Guid> { Guid.NewGuid() }
        };

        var scored = Assert.Single(_sut.Score([candidate], placeTags, []));

        Assert.Equal(0.5, scored.MatchScore);
    }

    [Fact]
    public void Score_PlaceWithoutTags_ReturnsZeroWhenTripHasTags()
    {
        var tripTags = new List<Guid> { Guid.NewGuid() };
        var candidate = MakeCandidate();

        var scored = Assert.Single(
            _sut.Score([candidate], new Dictionary<Guid, IReadOnlyList<Guid>>(), tripTags));

        Assert.Equal(0.0, scored.MatchScore);
    }

    private static PlaceCandidateDto MakeCandidate() =>
        new(
            PlaceId: Guid.NewGuid(),
            PlaceName: "Cà phê test",
            Address: "Địa chỉ",
            Latitude: 10.77,
            Longitude: 106.69,
            Category: "Cafe",
            EstimatedCostMin: 0,
            EstimatedCostMax: 50_000,
            ImageUrl: null,
            StationId: Guid.NewGuid(),
            StationName: "Bến Thành",
            StationOrder: 1,
            DistanceFromStationMeters: 100);
}