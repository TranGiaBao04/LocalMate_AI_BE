using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class SubscriptionService(
    IUserRepository userRepository,
    ISubscriptionRepository subscriptionRepository,
    IUsageEventRepository usageEventRepository,
    ITripRepository tripRepository,
    TimeProvider timeProvider) : ISubscriptionService
{
    public IReadOnlyList<SubscriptionPlanResponse> GetPlans() =>
        SubscriptionCatalog.All
            .Select(plan => new SubscriptionPlanResponse(
                plan.Code.ToString(),
                plan.Price,
                plan.DurationDays,
                plan.GenerateLimit,
                plan.SavedTripLimit))
            .ToArray();

    public async Task<SubscriptionMeResponse?> GetMySubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
        {
            return null;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var subscriptions = await subscriptionRepository.GetByUserIdAsync(userId, cancellationToken);
        var effective = SubscriptionCatalog.ResolveEffectivePaid(subscriptions, nowUtc);
        var plan = SubscriptionCatalog.Get(effective?.PlanCode ?? PlanCode.Free);
        var month = VietnamMonthWindow.For(nowUtc);
        var generateUsed = await usageEventRepository.CountAsync(
            userId,
            UsageEventType.Generate,
            month.StartUtc,
            month.NextStartUtc,
            cancellationToken);
        var savedTripsUsed = await tripRepository.CountFinalizedByUserAsync(userId, cancellationToken);

        return new SubscriptionMeResponse(
            plan.Code.ToString(),
            effective?.EndsAt,
            new SubscriptionUsageResponse(generateUsed, plan.GenerateLimit, month.NextStartUtc),
            new SubscriptionSavedTripsResponse(savedTripsUsed, plan.SavedTripLimit));
    }
}
