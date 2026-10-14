using LocalMateAI.Application.DTOs.Subscription;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlanGrantService
{
    Task<PlanGrantResult> GrantAsync(
        Guid userId,
        GrantPlanRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    // null = user không tồn tại.
    Task<IReadOnlyList<PlanGrantResponse>?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
