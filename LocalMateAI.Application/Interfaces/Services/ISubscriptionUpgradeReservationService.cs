using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Services;

// Internal financial primitive for UP-S4; deliberately absent from consumer controllers.
public interface ISubscriptionUpgradeReservationService
{
    Task<UpgradePreparationResult> PrepareAsync(Guid userId, string planCode, CancellationToken cancellationToken = default);
    Task<UpgradeReleaseResult> ResolveForReplacementAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default);
}
