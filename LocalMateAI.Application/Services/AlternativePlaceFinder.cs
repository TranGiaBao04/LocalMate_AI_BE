using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class AlternativePlaceFinder(ITagSimilarityScorer tagSimilarityScorer) : IAlternativePlaceFinder
{
    // Địa điểm thay thế được đắt hơn địa điểm hiện tại tối đa 25%.
    public const decimal CostTolerance = 1.25m;

    // Địa điểm miễn phí (0) chỉ khớp với địa điểm miễn phí vì 0 × 1.25 = 0.
    public static bool IsWithinCostTolerance(decimal currentCostMax, decimal candidateCostMax) =>
        candidateCostMax <= currentCostMax * CostTolerance;

    public IReadOnlyList<ScoredPlaceDto> Find(
        PlaceCandidateDto current,
        IReadOnlyList<PlaceCandidateDto> candidates,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> placeTagIdsByPlaceId,
        IReadOnlyCollection<Guid> excludedPlaceIds,
        int limit)
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
                && IsWithinCostTolerance(current.EstimatedCostMax, candidate.EstimatedCostMax))
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
