using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class SubscriptionRepository(AppDbContext dbContext) : ISubscriptionRepository
{
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
