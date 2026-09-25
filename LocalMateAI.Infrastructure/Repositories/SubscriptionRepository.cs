using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
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
}
