using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
namespace LocalMateAI.Tests;

// Adapts old fixture inputs into explicit legacy periods; production has no aggregate fallback.
public abstract class TestSubscriptionRepository : ISubscriptionRepository
{
    public List<SubscriptionPlan> Plans { get; } = [.. SubscriptionBaseline.Plans()];
    public List<SubscriptionPlanVersion> Versions { get; } = [.. SubscriptionBaseline.Versions()];
    public List<SubscriptionPeriod> Periods { get; } = [];
    public abstract Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    public abstract Task<UserSubscription?> GetByUserAndPlanAsync(Guid userId, PlanCode planCode, CancellationToken cancellationToken = default);
    public abstract Task AddAsync(UserSubscription subscription, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Plans);
    public Task<SubscriptionPlan?> GetPlanByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Plans.SingleOrDefault(p => p.Code == code));
    public Task<SubscriptionPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Plans.SingleOrDefault(p => p.Id == id));
    public Task<SubscriptionPlanVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Versions.SingleOrDefault(v => v.Id == id));
    public async Task<IReadOnlyList<SubscriptionPeriod>> GetPeriodsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var legacy = await GetByUserIdAsync(userId, cancellationToken);
        foreach (var s in legacy.Where(s => s.PlanCode != PlanCode.Free))
            if (!Periods.Any(p => p.LegacyUserSubscriptionId == s.Id))
                Periods.Add(new()
                {
                    Id = s.Id,
                    UserId = userId,
                    PlanId = SubscriptionBaseline.PlanId(s.PlanCode),
                    PlanVersionId = SubscriptionBaseline.VersionId(s.PlanCode),
                    StartsAt = s.StartsAt,
                    EndsAt = s.EndsAt,
                    LegacyUserSubscriptionId = s.Id
                });
        return Periods.Where(p => p.UserId == userId).ToArray();
    }
    public Task AddPeriodAsync(SubscriptionPeriod period, CancellationToken cancellationToken = default)
    {
        Periods.Add(period);
        return Task.CompletedTask;
    }
    public Task PublishVersionAsync(SubscriptionPlanVersion version, CancellationToken cancellationToken = default)
    {
        Versions.Add(version);
        Plans.Single(p => p.Id == version.PlanId).CurrentVersionId = version.Id;
        return Task.CompletedTask;
    }
    public static void Bind(PaymentOrder order)
    {
        if (order.PlanCode is not { } code || code == PlanCode.Free) return;
        order.PlanId = SubscriptionBaseline.PlanId(code);
        order.PlanVersionId = SubscriptionBaseline.VersionId(code);
        order.PlanVersionBinding = PlanVersionBinding.Native;
    }
}
