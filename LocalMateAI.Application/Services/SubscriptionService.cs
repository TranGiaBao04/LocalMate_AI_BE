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
    TimeProvider timeProvider,
    ILlmCallLogRepository llmCallLogs,
    ISystemSettingProvider settings) : ISubscriptionService
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
            var features = await subscriptionRepository.GetFeaturesForVersionAsync(id, cancellationToken);
            result.Add(new(PlanIdentity.PublicCode(plan.Code), version.Price, version.DurationDays,
                version.GenerateLimit, version.SavedTripLimit)
            {
                AiDailyCallLimit = version.AiDailyCallLimit,
                AiExplainCallsPerTripLimit = version.AiExplainCallsPerTripLimit,
                Features = features.Select(f => new SubscriptionFeatureResponse(f.Code, f.Name, f.Description)).ToArray()
            });
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
        var savedTripsUsed = await tripRepository.CountNormalFinalizedByUserAsync(userId, cancellationToken);
        var dayStartUtc = VietnamTime.StartOfDayUtc(nowUtc);
        var dailyUsed = await llmCallLogs.CountForUserSinceAsync(userId, dayStartUtc, cancellationToken);
        var dailyLimit = await AiEntitlementLimits.DailyAsync(plan, settings, cancellationToken);

        return new SubscriptionMeResponse(
            PlanIdentity.PublicCode(effective.Plan.Code),
            effective.PaidThrough,
            new SubscriptionUsageResponse(generateUsed, plan.GenerateLimit, effective.EffectiveUntil ?? month.NextStartUtc),
            new SubscriptionSavedTripsResponse(savedTripsUsed, plan.SavedTripLimit),
            new SubscriptionAiResponse(dailyUsed, dailyLimit, dayStartUtc.AddDays(1)))
        { EffectiveUntil = effective.EffectiveUntil };
    }
}
