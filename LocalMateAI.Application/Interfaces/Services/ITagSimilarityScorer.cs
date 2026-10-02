using LocalMateAI.Application.DTOs.Matching;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITagSimilarityScorer
{
    IReadOnlyList<ScoredPlaceDto> Score(
        IReadOnlyList<PlaceCandidateDto> candidates,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> placeTagIdsByPlaceId,
        IReadOnlyList<Guid> tripTagIds);
}