namespace LocalMateAI.Application.Interfaces.Repositories;

public sealed record TripGenerationQuotaExecution<T>(bool PersistedUserExists, T? Result);

public interface ITripGenerationQuotaExecutor
{
    Task<TripGenerationQuotaExecution<T>> ExecuteForUserAsync<T>(
        Guid userId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
