using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class UsageEventRepository(AppDbContext dbContext) : IUsageEventRepository
{
    public Task<int> CountForPeriodAsync(Guid userId, Guid periodId, UsageEventType type,
        CancellationToken cancellationToken = default) =>
        dbContext.UsageEvents.CountAsync(e => e.UserId == userId && e.SubscriptionPeriodId == periodId
            && e.Type == type, cancellationToken);
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
                                 && usage.SubscriptionPeriodId == null
                                 && usage.CreatedAt >= startUtc
                                 && usage.CreatedAt < nextStartUtc, cancellationToken);

    public async Task AddAsync(
        UsageEvent usageEvent,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "UsageEvents" ("Id", "CreatedAt", "TripId", "Type", "UpdatedAt", "UserId", "SubscriptionPeriodId")
            VALUES ({usageEvent.Id}, {usageEvent.CreatedAt}, {usageEvent.TripId},
                    {usageEvent.Type.ToString()}, {usageEvent.UpdatedAt}, {usageEvent.UserId}, {usageEvent.SubscriptionPeriodId})
            """,
            cancellationToken);
    }
}
