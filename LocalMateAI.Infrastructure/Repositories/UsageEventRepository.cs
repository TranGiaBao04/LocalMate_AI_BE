using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class UsageEventRepository(AppDbContext dbContext) : IUsageEventRepository
{
    public Task<int> CountAsync(
        Guid userId,
        UsageEventType type,
        DateTime startUtc,
        DateTime nextStartUtc,
        CancellationToken cancellationToken = default) =>
        dbContext.UsageEvents
            .AsNoTracking()
            .CountAsync(usage => usage.UserId == userId
                                 && usage.Type == type
                                 && usage.CreatedAt >= startUtc
                                 && usage.CreatedAt < nextStartUtc, cancellationToken);
}
