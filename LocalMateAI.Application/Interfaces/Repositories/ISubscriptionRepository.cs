using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ISubscriptionRepository
{
    Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
