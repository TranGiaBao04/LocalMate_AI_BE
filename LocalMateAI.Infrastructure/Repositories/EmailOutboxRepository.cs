using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class EmailOutboxRepository(AppDbContext dbContext) : IEmailOutboxRepository
{
    private static readonly string PendingStatus = nameof(EmailOutboxStatus.Pending);

    public async Task<bool> EnqueueAsync(
        EmailOutboxEntry entry,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        // ON CONFLICT thay cho bắt lỗi unique: lỗi unique trong Postgres làm hỏng cả transaction đang mở
        // (ví dụ transaction thanh toán) và buộc rollback.
        var inserted = await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "EmailOutboxMessages"
                ("Id", "ToEmail", "Subject", "TemplateName", "Model", "Status",
                 "AttemptCount", "NextAttemptAt", "SentAt", "DeduplicationKey", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {entry.ToEmail}, {entry.Subject}, {entry.TemplateName},
                 CAST({entry.ModelJson} AS jsonb), {PendingStatus},
                 0, {now}, NULL, {entry.DeduplicationKey}, {now}, {now})
            ON CONFLICT ("DeduplicationKey") DO NOTHING
            """,
            cancellationToken);

        return inserted == 1;
    }

    public async Task<IReadOnlyList<ClaimedEmailOutboxMessage>> ClaimDueAsync(
        DateTime now,
        DateTime leaseUntil,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Một câu UPDATE: chọn + khoá (bỏ qua dòng instance khác đang khoá) + giữ chỗ.
        // Nếu app sập khi đang gửi, mail tự đến hạn lại sau leaseUntil.
        var rows = await dbContext.Database.SqlQuery<ClaimedRow>(
                $"""
                UPDATE "EmailOutboxMessages" AS message
                SET "AttemptCount" = message."AttemptCount" + 1,
                    "NextAttemptAt" = {leaseUntil},
                    "UpdatedAt" = {now}
                WHERE message."Id" IN (
                    SELECT "Id" FROM "EmailOutboxMessages"
                    WHERE "Status" = {PendingStatus} AND "NextAttemptAt" <= {now}
                    ORDER BY "NextAttemptAt"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING message."Id", message."ToEmail", message."Subject", message."TemplateName",
                          message."Model"::text AS "Model", message."AttemptCount"
                """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ClaimedEmailOutboxMessage(
                row.Id,
                row.ToEmail,
                row.Subject,
                row.TemplateName,
                row.Model,
                row.AttemptCount))
            .ToList();
    }

    public Task MarkSentAsync(Guid id, DateTime now, CancellationToken cancellationToken = default) =>
        dbContext.EmailOutboxMessages
            .Where(message => message.Id == id && message.Status == EmailOutboxStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, EmailOutboxStatus.Sent)
                .SetProperty(message => message.SentAt, now)
                .SetProperty(message => message.UpdatedAt, now), cancellationToken);

    public Task ScheduleRetryAsync(
        Guid id,
        DateTime nextAttemptAt,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        dbContext.EmailOutboxMessages
            .Where(message => message.Id == id && message.Status == EmailOutboxStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.NextAttemptAt, nextAttemptAt)
                .SetProperty(message => message.UpdatedAt, now), cancellationToken);

    public Task MarkFailedAsync(Guid id, DateTime now, CancellationToken cancellationToken = default) =>
        dbContext.EmailOutboxMessages
            .Where(message => message.Id == id && message.Status == EmailOutboxStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, EmailOutboxStatus.Failed)
                .SetProperty(message => message.UpdatedAt, now), cancellationToken);

    public Task<int> DeleteExpiredAsync(
        DateTime sentBefore,
        DateTime failedBefore,
        CancellationToken cancellationToken = default) =>
        dbContext.EmailOutboxMessages
            .Where(message =>
                (message.Status == EmailOutboxStatus.Sent && message.SentAt < sentBefore)
                || (message.Status == EmailOutboxStatus.Failed && message.UpdatedAt < failedBefore))
            .ExecuteDeleteAsync(cancellationToken);

    private sealed record ClaimedRow(
        Guid Id,
        string ToEmail,
        string Subject,
        string TemplateName,
        string Model,
        int AttemptCount);
}
