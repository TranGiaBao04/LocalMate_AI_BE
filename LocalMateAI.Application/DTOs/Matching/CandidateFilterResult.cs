namespace LocalMateAI.Application.DTOs.Matching;

public sealed record CandidateFilterResult(
    IReadOnlyList<PlaceCandidateDto> Passed,
    IReadOnlyList<ExcludedCandidateDto> Excluded);

public sealed record ExcludedCandidateDto(
    Guid PlaceId,
    string PlaceName,
    string Reason); // "budget" | "insufficient_slots"