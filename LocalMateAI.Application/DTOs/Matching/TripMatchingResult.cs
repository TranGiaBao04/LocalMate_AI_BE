namespace LocalMateAI.Application.DTOs.Matching;

public sealed record TripMatchingResponse(
    bool IsSufficient,
    string? InsufficiencyReason, // "OutOfServiceArea" | "InsufficientCandidates" | null
    string? StationName,
    int EstimatedStopCount,
    string BudgetTier,
    IReadOnlyList<ScoredPlaceDto> Candidates,
    IReadOnlyList<ExcludedCandidateDto> Excluded);

public sealed record TripMatchingResult(
    TripMatchingResultStatus Status,
    TripMatchingResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public enum TripMatchingResultStatus
{
    Success,
    ValidationFailed
}