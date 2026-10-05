using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

public static class AiEntitlementLimits
{
    public static Task<int> DailyAsync(SubscriptionPlanVersion version,
        ISystemSettingProvider settings, CancellationToken cancellationToken = default) =>
        version.AiDailyCallLimit is { } limit
            ? Task.FromResult(limit)
            : settings.GetIntAsync(SystemSettingKeys.AiDailyCallsPerUser, cancellationToken);

    public static Task<int> ExplainPerTripAsync(SubscriptionPlanVersion version,
        ISystemSettingProvider settings, CancellationToken cancellationToken = default) =>
        version.AiExplainCallsPerTripLimit is { } limit
            ? Task.FromResult(limit)
            : settings.GetIntAsync(SystemSettingKeys.AiExplainCallsPerTrip, cancellationToken);
}
