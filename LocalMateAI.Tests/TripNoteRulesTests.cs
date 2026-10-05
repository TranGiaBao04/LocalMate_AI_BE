using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TripNoteRulesTests
{
    private const int Floor = 66;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \t \n ")]
    public void Normalize_BlankNote_ReturnsNull(string? note) =>
        Assert.Null(TripNoteRules.Normalize(note));

    [Fact]
    public void Normalize_TrimsAndCollapsesWhitespace_KeepingCase() =>
        Assert.Equal("Muốn chỗ Yên Tĩnh", TripNoteRules.Normalize("  Muốn   chỗ \n Yên Tĩnh  "));

    [Fact]
    public void Score_GrowsWithTheMarginAboveFloor_AndCapsAtOne()
    {
        var atFloor = Guid.NewGuid();
        var halfway = Guid.NewGuid();
        var atFullMargin = Guid.NewGuid();
        var farAbove = Guid.NewGuid();
        var similarities = new Dictionary<Guid, double>
        {
            [atFloor] = 0.66,
            [halfway] = 0.71,
            [atFullMargin] = 0.76,
            [farAbove] = 0.90
        };

        var scores = TripNoteRules.Score(similarities.Keys, similarities, Floor);

        Assert.Equal(0d, scores[atFloor], 9);
        Assert.Equal(0.5, scores[halfway], 9);
        Assert.Equal(1d, scores[atFullMargin], 9);
        Assert.Equal(1d, scores[farAbove], 9);
    }

    [Fact]
    public void Score_LeavesOutCandidatesBelowFloor_OrWithoutAVector()
    {
        var matching = Guid.NewGuid();
        var belowFloor = Guid.NewGuid();
        var noVector = Guid.NewGuid();
        var similarities = new Dictionary<Guid, double> { [matching] = 0.70, [belowFloor] = 0.659 };

        var scores = TripNoteRules.Score([matching, belowFloor, noVector], similarities, Floor);

        Assert.Equal([matching], scores.Keys);
    }

    [Fact]
    public void Score_IgnoresPlacesThatAreNotCandidates()
    {
        var notACandidate = Guid.NewGuid();
        var similarities = new Dictionary<Guid, double> { [notACandidate] = 0.90 };

        Assert.Empty(TripNoteRules.Score([Guid.NewGuid()], similarities, Floor));
    }

    [Fact]
    public void Score_UsesTheFloorItIsGiven()
    {
        var place = Guid.NewGuid();
        var similarities = new Dictionary<Guid, double> { [place] = 0.60 };

        Assert.Empty(TripNoteRules.Score([place], similarities, Floor));
        Assert.Equal(1d, TripNoteRules.Score([place], similarities, minSimilarityPercent: 50)[place], 9);
    }
}
