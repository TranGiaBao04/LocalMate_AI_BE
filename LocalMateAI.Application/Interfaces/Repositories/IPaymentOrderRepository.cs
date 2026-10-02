using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPaymentOrderRepository
{
    Task<PaymentOrder?> CompleteUpgradeLinkAsync(Guid userId, Guid orderId, PaymentLinkResult link, DateTime nowUtc,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<PaymentOrder?> GetBlockingSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PaymentOrder?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetExpiredPendingIdsAsync(DateTime nowUtc, int batchSize,
        CancellationToken cancellationToken = default);
    Task<PaymentOrder?> GetPendingAsync(
        Guid userId,
        Guid planId,
        PaymentOrderType type,
        CancellationToken cancellationToken = default);

    Task<PaymentOrder?> GetOwnedByIdAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkExpiredIfPendingAsync(
        Guid orderId,
        DateTime updatedAt,
        CancellationToken cancellationToken = default);

    Task AddAsync(PaymentOrder order, CancellationToken cancellationToken = default);

    Task TransitionStatusAsync(PaymentOrder order, PaymentOrderStatus status, PaymentTransitionContext context,
        DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<bool> MarkExpiredIfPendingAsync(Guid orderId, DateTime nowUtc, PaymentTransitionContext context,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
