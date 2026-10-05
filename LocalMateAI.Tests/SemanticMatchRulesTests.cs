using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class SemanticMatchRulesTests
{
    private const int Floor = 66;
    private const int Gap = 4;

    [Fact]
    public void NoScores_ReturnsEmpty()
    {
        Assert.Empty(SemanticMatchRules.Select(new Dictionary<Guid, double>(), Floor, Gap));
    }

    [Fact]
    public void TopScoreBelowFloor_ReturnsEmpty()
    {
        // "asdf qwerty" ở GĐ 0: điểm cao nhất 0,601.
        var scores = Scores(0.601, 0.598, 0.597);

        Assert.Empty(SemanticMatchRules.Select(scores, Floor, Gap));
    }

    [Fact]
    public void PlacesFarBelowTheTop_AreDropped_EvenWhenAboveFloor()
    {
        // "ăn pizza" ở GĐ 0: Pizza 4P's 0,742; Cơm tấm 0,679 và Bột chiên 0,669 đều qua sàn nhưng quá xa hạng 1.
        var scores = Scores(0.742, 0.679, 0.669);

        var match = Assert.Single(SemanticMatchRules.Select(scores, Floor, Gap));

        Assert.Equal(0.742, match.Score);
    }

    [Fact]
    public void PlacesCloseToTheTop_AreAllKept_InDescendingOrder()
    {
        // "ăn vặt vỉa hè giá rẻ" ở GĐ 0: ba quán đầu sát nhau, quán thứ tư cách hạng 1 quá 4 điểm.
        var scores = Scores(0.716, 0.685, 0.753, 0.728);

        var matches = SemanticMatchRules.Select(scores, Floor, Gap);

        Assert.Equal([0.753, 0.728, 0.716], matches.Select(match => match.Score));
    }

    [Fact]
    public void LargeGap_StillNeverGoesBelowFloor()
    {
        var scores = Scores(0.70, 0.67, 0.65, 0.40);

        var matches = SemanticMatchRules.Select(scores, Floor, maxGapFromTopPercent: 100);

        Assert.Equal([0.70, 0.67], matches.Select(match => match.Score));
    }

    [Fact]
    public void ZeroGap_KeepsOnlyTheTopScore()
    {
        var scores = Scores(0.80, 0.799, 0.70);

        Assert.Equal([0.80], SemanticMatchRules.Select(scores, Floor, maxGapFromTopPercent: 0).Select(match => match.Score));
    }

    [Fact]
    public void ScoreExactlyAtFloor_IsKept()
    {
        Assert.Single(SemanticMatchRules.Select(Scores(0.66), Floor, Gap));
    }

    [Fact]
    public void EqualScores_AreOrderedByPlaceId_SoPagingIsStable()
    {
        var first = new Guid("00000000-0000-0000-0000-000000000001");
        var second = new Guid("00000000-0000-0000-0000-000000000002");
        var scores = new Dictionary<Guid, double> { [second] = 0.7, [first] = 0.7 };

        Assert.Equal([first, second], SemanticMatchRules.Select(scores, Floor, Gap).Select(match => match.PlaceId));
    }

    private static Dictionary<Guid, double> Scores(params double[] values) =>
        values.ToDictionary(_ => Guid.NewGuid(), value => value);
}
