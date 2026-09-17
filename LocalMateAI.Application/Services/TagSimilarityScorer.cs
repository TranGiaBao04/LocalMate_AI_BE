using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TagSimilarityScorer : ITagSimilarityScorer
{
    private const double NeutralScore = 0.5;

    public IReadOnlyList<ScoredPlaceDto> Score(
        IReadOnlyList<PlaceCandidateDto> candidates,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> placeTagIdsByPlaceId,
        IReadOnlyList<Guid> tripTagIds)
    {
        var tripSet = tripTagIds.ToHashSet();

        return candidates.Select(candidate =>
        {
            var placeTagIds = placeTagIdsByPlaceId.GetValueOrDefault(candidate.PlaceId) ?? [];
            var placeSet = placeTagIds.ToHashSet();

            double score;
            IReadOnlyList<Guid> matched = [];

            if (tripSet.Count == 0)
            {
                score = NeutralScore;
            }
            else if (placeSet.Count == 0)
            {
                score = 0;
            }
            else
            {
                matched = tripSet.Intersect(placeSet).ToList();
                score = (double)matched.Count / tripSet.Count;
            }

            return new ScoredPlaceDto(candidate, score, matched);
        }).ToList();
    }
}