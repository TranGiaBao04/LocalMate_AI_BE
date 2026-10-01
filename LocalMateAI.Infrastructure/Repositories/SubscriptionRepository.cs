using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class SubscriptionRepository(AppDbContext dbContext) : ISubscriptionRepository
{
    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        await dbContext.SubscriptionPlans.AsNoTracking().OrderBy(p => p.EntitlementPriority)
            .ToListAsync(cancellationToken);

    public Task<SubscriptionPlan?> GetPlanByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Code == code, cancellationToken);

    public Task<SubscriptionPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<SubscriptionPlanVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPlanVersions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionPeriod>> GetPeriodsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.SubscriptionPeriods.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(cancellationToken);

    public Task AddPeriodAsync(SubscriptionPeriod period, CancellationToken cancellationToken = default) =>
        dbContext.SubscriptionPeriods.AddAsync(period, cancellationToken).AsTask();

    // Serializing publication on the identity prevents two concurrent versions from sharing a number/pointer.
    public async Task PublishVersionAsync(SubscriptionPlanVersion version, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SubscriptionPlans\" WHERE \"Id\" = {version.PlanId} FOR UPDATE", cancellationToken);
        var plan = await dbContext.SubscriptionPlans.SingleAsync(p => p.Id == version.PlanId, cancellationToken);
        var latest = await dbContext.SubscriptionPlanVersions.Where(v => v.PlanId == plan.Id)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0;
        if (version.VersionNumber != latest + 1 || version.Origin != PlanVersionOrigin.Published
            || version.PublishedAt is null
            || (plan.Code == "FREE" ? version.Price != 0 || version.DurationDays != null
                : version.Price <= 0 || version.DurationDays is null))
            throw new InvalidOperationException("Invalid published plan version.");
        dbContext.SubscriptionPlanVersions.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);
        plan.CurrentVersionId = version.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .ToListAsync(cancellationToken);

    public Task<UserSubscription?> GetByUserAndPlanAsync(
        Guid userId,
        PlanCode planCode,
        CancellationToken cancellationToken = default) =>
        dbContext.UserSubscriptions.SingleOrDefaultAsync(
            subscription => subscription.UserId == userId
                            && subscription.PlanCode == planCode,
            cancellationToken);

    public Task AddAsync(
        UserSubscription subscription,
        CancellationToken cancellationToken = default) =>
        dbContext.UserSubscriptions.AddAsync(subscription, cancellationToken).AsTask();
}
