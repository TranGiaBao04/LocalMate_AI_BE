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
}
