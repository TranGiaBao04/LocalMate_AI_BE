namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record SubscriptionUsageResponse(int GenerateUsed, int? GenerateLimit, DateTime ResetAt);

public sealed record SubscriptionSavedTripsResponse(int Used, int? Limit);

public sealed record SubscriptionMeResponse(
    string Plan,
    DateTime? EndsAt,
    SubscriptionUsageResponse Usage,
    SubscriptionSavedTripsResponse SavedTrips);
