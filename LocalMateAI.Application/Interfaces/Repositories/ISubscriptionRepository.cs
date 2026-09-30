using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ISubscriptionRepository
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetPlanByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<SubscriptionPlan?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionPeriod>> GetPeriodsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddPeriodAsync(SubscriptionPeriod period, CancellationToken cancellationToken = default);
    Task PublishVersionAsync(SubscriptionPlanVersion version, CancellationToken cancellationToken = default);
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
