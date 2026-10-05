namespace LocalMateAI.Application.Services;

public sealed record SemanticMatch(Guid PlaceId, double Score);

// Nơi DUY NHẤT quyết định địa điểm nào được coi là "khớp theo nghĩa" từ điểm tương đồng thô.
public static class SemanticMatchRules
{
    /// <summary>
    /// Luật 1 (sàn): điểm cao nhất dưới sàn thì không địa điểm nào khớp.
    /// Luật 2 (khoảng cách): chỉ giữ địa điểm đạt sàn và cách điểm cao nhất không quá maxGapFromTopPercent.
    /// Trả theo điểm giảm dần.
    /// </summary>
    public static IReadOnlyList<SemanticMatch> Select(
        IReadOnlyDictionary<Guid, double> scores,
        int minSimilarityPercent,
        int maxGapFromTopPercent)
    {
        if (scores.Count == 0)
        {
            return [];
        }

        var floor = minSimilarityPercent / 100d;
        var top = scores.Values.Max();

        if (top < floor)
        {
            return [];
        }

        var cutoff = Math.Max(floor, top - maxGapFromTopPercent / 100d);

        return scores
            .Where(pair => pair.Value >= cutoff)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => new SemanticMatch(pair.Key, pair.Value))
            .ToList();
    }
}
