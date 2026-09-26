using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ISubscriptionRepository
{
    Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserSubscription?> GetByUserAndPlanAsync(
        Guid userId,
        PlanCode planCode,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        UserSubscription subscription,
        CancellationToken cancellationToken = default);
}
