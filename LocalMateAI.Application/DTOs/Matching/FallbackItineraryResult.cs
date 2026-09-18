namespace LocalMateAI.Application.DTOs.Matching;

public sealed record FallbackItineraryResult(
    FallbackItineraryStatus Status,
    FallbackItineraryPayload? Payload = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed record FallbackItineraryPayload(
    string FallbackReason, // "llm_timeout" | "llm_error" | "heuristic"
    bool IsSufficient,
    string? InsufficiencyReason, // "OutOfServiceArea" | "InsufficientCandidates" | null
    string StationName,
    int EstimatedStopCount,
    string BudgetTier,
    IReadOnlyList<FallbackStopDto> Stops);

public enum FallbackItineraryStatus
{
    Success,
    ValidationFailed
}