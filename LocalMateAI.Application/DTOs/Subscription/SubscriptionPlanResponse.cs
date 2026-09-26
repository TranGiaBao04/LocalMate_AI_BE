namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record SubscriptionPlanResponse(
    string Code,
    decimal Price,
    int? DurationDays,
    int? GenerateLimit,
    int? SavedTripLimit);
