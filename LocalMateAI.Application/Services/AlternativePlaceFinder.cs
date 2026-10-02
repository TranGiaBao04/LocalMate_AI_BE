using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class AlternativePlaceFinder(ITagSimilarityScorer tagSimilarityScorer) : IAlternativePlaceFinder
{
    // Ứng viên được đắt hơn địa điểm hiện tại tối đa maxCostIncreasePercent % (SystemSettings, mặc định 25).
    // So bằng phép nhân chéo, chỉ dùng số nguyên. Miễn phí (0) chỉ khớp miễn phí.
    public static bool IsWithinCostTolerance(decimal currentCostMax, decimal candidateCostMax,
        int maxCostIncreasePercent) =>
        candidateCostMax * 100 <= currentCostMax * (100 + maxCostIncreasePercent);

    public IReadOnlyList<ScoredPlaceDto> Find(
        PlaceCandidateDto current,
        IReadOnlyList<PlaceCandidateDto> candidates,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> placeTagIdsByPlaceId,
        IReadOnlyCollection<Guid> excludedPlaceIds,
        int limit,
        int maxCostIncreasePercent)
    {
        if (limit <= 0)
        {
            return [];
        }

        var excluded = excludedPlaceIds.ToHashSet();
        excluded.Add(current.PlaceId);

        var pool = candidates
            .Where(candidate => candidate.StationId == current.StationId
                && !excluded.Contains(candidate.PlaceId)
                && IsWithinCostTolerance(current.EstimatedCostMax, candidate.EstimatedCostMax, maxCostIncreasePercent))
            .ToList();

        var currentTagIds = placeTagIdsByPlaceId.GetValueOrDefault(current.PlaceId) ?? [];

        return tagSimilarityScorer.Score(pool, placeTagIdsByPlaceId, currentTagIds)
            .OrderByDescending(scored => scored.MatchScore)
            .ThenByDescending(scored => scored.Candidate.Category == current.Category)
            .ThenBy(scored => Math.Abs(scored.Candidate.EstimatedCostMax - current.EstimatedCostMax))
            .ThenBy(scored => scored.Candidate.DistanceFromStationMeters)
            .ThenBy(scored => scored.Candidate.PlaceId)
            .Take(limit)
            .ToList();
    }
}
