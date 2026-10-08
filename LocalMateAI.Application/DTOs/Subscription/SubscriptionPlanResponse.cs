namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record SubscriptionPlanResponse(
    string Code,
    decimal Price,
    int? DurationDays,
    int? GenerateLimit,
    int? SavedTripLimit)
{
    public int? AiDailyCallLimit { get; init; }
    public int? AiExplainCallsPerTripLimit { get; init; }
    public IReadOnlyList<SubscriptionFeatureResponse> Features { get; init; } = [];
}
