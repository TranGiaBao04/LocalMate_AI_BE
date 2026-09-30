using LocalMateAI.Application.DTOs.Subscription;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionMeResponse?> GetMySubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
