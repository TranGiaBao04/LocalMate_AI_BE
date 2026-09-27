using LocalMateAI.Application.DTOs.Subscription;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ISubscriptionService
{
    IReadOnlyList<SubscriptionPlanResponse> GetPlans();

    Task<SubscriptionMeResponse?> GetMySubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
