using LocalMateAI.Domain.Entities;
using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Repositories;

public sealed record PaymentSettlementExecution<T>(bool OrderExists, T? Result);

public interface IPaymentSettlementExecutor
{
    Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(long providerOrderCode, PaymentTransitionContext context,
        Func<PaymentOrder, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
    Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(
        long providerOrderCode,
        Func<PaymentOrder, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
