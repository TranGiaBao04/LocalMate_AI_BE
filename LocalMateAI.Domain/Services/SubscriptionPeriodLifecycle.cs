using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Domain.Services;

public static class SubscriptionPeriodLifecycle
{
    public static DateTime EffectiveEnd(SubscriptionPeriod period) =>
        EffectiveEnd(period.EndsAt, period.TerminatedAt);

    public static DateTime EffectiveEnd(DateTime endsAt, DateTime? terminatedAt) =>
        terminatedAt is { } at && at < endsAt ? at : endsAt;

    public static bool HasEffectiveDuration(SubscriptionPeriod period) =>
        period.StartsAt < EffectiveEnd(period);

    public static bool IsEffectiveAt(SubscriptionPeriod period, DateTime atUtc) =>
        period.StartsAt <= atUtc && atUtc < EffectiveEnd(period);
}
