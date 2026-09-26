using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPaymentOrderRepository
{
    Task<PaymentOrder?> GetPendingAsync(
        Guid userId,
        PlanCode planCode,
        PaymentOrderType type,
        CancellationToken cancellationToken = default);

    Task<PaymentOrder?> GetOwnedByIdAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task AddAsync(PaymentOrder order, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
