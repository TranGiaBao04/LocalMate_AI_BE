using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public sealed record PaymentSettlementExecution<T>(bool OrderExists, T? Result);

public interface IPaymentSettlementExecutor
{
    Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(
        long providerOrderCode,
        Func<PaymentOrder, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
