namespace LocalMateAI.Application.DTOs.Matching;

public sealed record ScoredPlaceDto(
    PlaceCandidateDto Candidate,
    double MatchScore, // 0..1
    IReadOnlyList<Guid> MatchedTagIds);