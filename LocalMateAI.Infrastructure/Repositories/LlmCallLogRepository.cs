using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class LlmCallLogRepository(AppDbContext context) : ILlmCallLogRepository
{
    public async Task AddAsync(LlmCallLog log, CancellationToken cancellationToken = default)
    {
        context.LlmCallLogs.Add(log);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountForUserSinceAsync(
        Guid userId,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        context.LlmCallLogs
            .AsNoTracking()
            .CountAsync(log => log.UserId == userId && log.CreatedAt >= sinceUtc, cancellationToken);

    public Task<int> CountSucceededForTripAsync(
        Guid tripId,
        LlmCallKind kind,
        CancellationToken cancellationToken = default) =>
        context.LlmCallLogs
            .AsNoTracking()
            .CountAsync(
                log => log.TripId == tripId && log.Kind == kind && log.Outcome == LlmCallOutcome.Succeeded,
                cancellationToken);
}
