namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAiUsageAccountingRepository
{
    Task<int> CountDailyAsync(Guid userId, DateOnly vietnamUsageDate,
        CancellationToken cancellationToken = default);
}
