using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IUsageEventRepository
{
    Task<int> CountAsync(
        Guid userId,
        UsageEventType type,
        DateTime startUtc,
        DateTime nextStartUtc,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        UsageEvent usageEvent,
        CancellationToken cancellationToken = default);
}
