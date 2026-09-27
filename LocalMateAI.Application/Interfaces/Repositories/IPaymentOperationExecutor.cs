namespace LocalMateAI.Application.Interfaces.Repositories;

public sealed record PaymentOperationExecution<T>(bool PersistedUserExists, T? Result);

public interface IPaymentOperationExecutor
{
    Task<PaymentOperationExecution<T>> ExecuteForUserAsync<T>(
        Guid userId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
