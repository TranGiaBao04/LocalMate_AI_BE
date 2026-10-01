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
    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SubscriptionPlanResponse>();
        foreach (var plan in await subscriptionRepository.GetPlansAsync(cancellationToken))
        {
            // Consumer discovery remains built-in only until custom-plan UI is ready.
            if (!plan.IsSystem || !plan.IsActive || plan.CurrentVersionId is not { } id) continue;
            var version = await subscriptionRepository.GetVersionAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("Missing current version.");
            result.Add(new(PlanIdentity.PublicCode(plan.Code), version.Price, version.DurationDays,
                version.GenerateLimit, version.SavedTripLimit));
        }
        return result;
    }

    public async Task<SubscriptionMeResponse?> GetMySubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
        {
            return null;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var effective = await EffectiveSubscriptionResolver.ResolveAsync(subscriptionRepository, userId, nowUtc, cancellationToken);
        var plan = effective.Version;
        var month = VietnamMonthWindow.For(nowUtc);
        var generateUsed = await EffectiveSubscriptionResolver.CountGenerateAsync(
            usageEventRepository, userId, effective, nowUtc, cancellationToken);
        var savedTripsUsed = await tripRepository.CountFinalizedByUserAsync(userId, cancellationToken);

        return new SubscriptionMeResponse(
            PlanIdentity.PublicCode(effective.Plan.Code),
            effective.PaidThrough,
            new SubscriptionUsageResponse(generateUsed, plan.GenerateLimit, effective.EffectiveUntil ?? month.NextStartUtc),
            new SubscriptionSavedTripsResponse(savedTripsUsed, plan.SavedTripLimit))
        { EffectiveUntil = effective.EffectiveUntil };
    }
}
