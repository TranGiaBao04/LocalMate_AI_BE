using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminPlanRepository
{
    Task<PagedResult<AdminPlanResponse>> GetPlansAsync(AdminPlanQuery query, DateTime nowUtc,
        CancellationToken cancellationToken = default);
    Task<AdminPlanResponse?> GetPlanAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminPlanVersionResponse>> GetVersionsAsync(Guid planId, AdminPlanVersionsQuery query,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminPlanFeatureResponse>> GetFeaturesAsync(CancellationToken cancellationToken = default);
    Task<bool> FeaturesExistAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
    Task<SubscriptionPlanVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetVersionFeatureIdsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminPlanResultStatus> CreateAsync(SubscriptionPlan plan, PlanVersionTerms terms, DateTime nowUtc,
        CancellationToken cancellationToken = default);
    Task<T> ExecuteLockedAsync<T>(Guid id, Func<SubscriptionPlan?, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
    // Must run inside ExecuteLockedAsync, or the transaction creating the plan.
    Task PublishNextVersionAsync(SubscriptionPlan plan, PlanVersionTerms terms, DateTime nowUtc,
        CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
    Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> TryDeleteAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default);
}
