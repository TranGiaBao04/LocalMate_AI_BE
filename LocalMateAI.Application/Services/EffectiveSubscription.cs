using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
namespace LocalMateAI.Application.Services;

public sealed record EffectiveSubscription(SubscriptionPlan Plan, SubscriptionPlanVersion Version,
    SubscriptionPeriod? Period, DateTime? PaidThrough)
{
    public DateTime? EffectiveUntil => Period?.EndsAt;
}
public static class EffectiveSubscriptionResolver
{
    public static async Task<EffectiveSubscription> ResolveAsync(ISubscriptionRepository repository,
        Guid userId, DateTime nowUtc, CancellationToken ct = default)
    {
        var periods = await repository.GetPeriodsAsync(userId, ct);
        var active = periods.Where(p => p.StartsAt <= nowUtc && nowUtc < p.EndsAt).ToArray();
        var candidates = new List<EffectiveSubscription>();
        foreach (var period in active)
        {
            var plan = await repository.GetPlanAsync(period.PlanId, ct)
                ?? throw new InvalidOperationException("Missing period plan.");
            var version = await repository.GetVersionAsync(period.PlanVersionId, ct)
                ?? throw new InvalidOperationException("Missing purchased version.");
            if (version.PlanId != plan.Id || plan.Code == PlanIdentity.Free)
                throw new InvalidOperationException("Invalid paid entitlement binding.");
            var paidThrough = period.EndsAt;
            foreach (var next in periods.Where(p => p.PlanId == plan.Id && p.StartsAt >= period.EndsAt)
                         .OrderBy(p => p.StartsAt))
            {
                if (next.StartsAt != paidThrough) break;
                paidThrough = next.EndsAt;
            }
            candidates.Add(new(plan, version, period, paidThrough));
        }
        if (candidates.Count > 0)
            return candidates.OrderByDescending(c => c.Plan.EntitlementPriority).First();
        var free = await repository.GetPlanByCodeAsync(PlanIdentity.Free, ct)
            ?? throw new InvalidOperationException("Free plan is not configured.");
        var current = free.CurrentVersionId is { } id
            ? await repository.GetVersionAsync(id, ct) : null;
        if (!free.IsActive || current is null || current.PlanId != free.Id)
            throw new InvalidOperationException("Free version is not configured.");
        return new(free, current, null, null);
    }

    public static Task<int> CountGenerateAsync(IUsageEventRepository usage, Guid userId,
        EffectiveSubscription effective, DateTime nowUtc, CancellationToken ct = default)
    {
        if (effective.Period is { } period)
            return effective.Version.GenerateLimit is null ? Task.FromResult(0)
                : usage.CountForPeriodAsync(userId, period.Id, UsageEventType.Generate, ct);
        var month = VietnamMonthWindow.For(nowUtc);
        return usage.CountAsync(userId, UsageEventType.Generate, month.StartUtc, month.NextStartUtc, ct);
    }
}
