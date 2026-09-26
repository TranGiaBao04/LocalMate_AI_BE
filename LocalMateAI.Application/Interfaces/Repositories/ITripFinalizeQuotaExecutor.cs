namespace LocalMateAI.Application.Interfaces.Repositories;

public sealed record TripFinalizeQuotaExecution<T>(bool PersistedUserExists, T? Result);

public interface ITripFinalizeQuotaExecutor
{
    Task<TripFinalizeQuotaExecution<T>> ExecuteForUserAsync<T>(
        Guid userId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
