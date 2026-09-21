using LocalMateAI.Application.DTOs.Matching;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAlternativePlaceFinder
{
    // placeTagIdsByPlaceId phải chứa cả tag của địa điểm hiện tại (caller nạp sẵn).
    IReadOnlyList<ScoredPlaceDto> Find(
        PlaceCandidateDto current,
        IReadOnlyList<PlaceCandidateDto> candidates,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> placeTagIdsByPlaceId,
        IReadOnlyCollection<Guid> excludedPlaceIds,
        int limit);
}
