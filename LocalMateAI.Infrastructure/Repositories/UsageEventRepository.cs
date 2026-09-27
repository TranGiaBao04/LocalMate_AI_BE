using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
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

    public async Task AddAsync(
        UsageEvent usageEvent,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "UsageEvents" ("Id", "CreatedAt", "TripId", "Type", "UpdatedAt", "UserId")
            VALUES ({usageEvent.Id}, {usageEvent.CreatedAt}, {usageEvent.TripId},
                    {usageEvent.Type.ToString()}, {usageEvent.UpdatedAt}, {usageEvent.UserId})
            """,
            cancellationToken);
    }
}
