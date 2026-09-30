using LocalMateAI.Application.DTOs.Plans;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminPlanService
{
    Task<AdminPlanQueryResult<AdminPlanResponse>> GetPlansAsync(AdminPlanQuery query,
        CancellationToken cancellationToken = default);
    Task<AdminPlanResponse?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminPlanFeatureResponse>> GetFeaturesAsync(CancellationToken cancellationToken = default);
    Task<AdminPlanQueryResult<AdminPlanVersionResponse>> GetVersionsAsync(Guid id, AdminPlanVersionsQuery query,
        CancellationToken cancellationToken = default);
    Task<AdminPlanResult> CreateAsync(CreateAdminPlanRequest request, CancellationToken cancellationToken = default);
    Task<AdminPlanResult> UpdateAsync(Guid id, UpdateAdminPlanRequest request, CancellationToken cancellationToken = default);
    Task<AdminPlanResult> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<AdminPlanResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
