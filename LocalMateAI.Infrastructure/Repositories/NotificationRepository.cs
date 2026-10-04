using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class NotificationRepository(AppDbContext dbContext) : INotificationRepository
{
    private static readonly SortMap<Notification> NotificationSortMap =
        new SortMap<Notification>("createdAt", true, notification => notification.Id)
            .Add("createdAt", notification => notification.CreatedAt);

    public async Task<bool> EnqueueAsync(
        NotificationEntry entry,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var targetType = entry.TargetType.ToString();

        // ON CONFLICT thay cho bắt lỗi unique: lỗi unique trong Postgres làm hỏng cả transaction đang mở
        // (chốt lịch trình, thanh toán) và buộc rollback.
        var inserted = await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "Notifications"
                ("Id", "UserId", "Type", "Title", "Body", "TargetType", "TargetId",
                 "ReadAt", "DeduplicationKey", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {entry.UserId}, {entry.Type}, {entry.Title}, {entry.Body}, {targetType},
                 CAST({entry.TargetId} AS uuid), NULL, {entry.DeduplicationKey}, {now}, {now})
            ON CONFLICT ("DeduplicationKey") DO NOTHING
            """,
            cancellationToken);

        return inserted == 1;
    }

    public Task<PagedResult<NotificationResponse>> GetPagedAsync(
        Guid userId,
        NotificationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notifications = dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId);

        if (query.UnreadOnly)
        {
            notifications = notifications.Where(notification => notification.ReadAt == null);
        }

        return notifications
            .ApplySort(query, NotificationSortMap)
            .Select(notification => new NotificationResponse(
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Body,
                notification.TargetType,
                notification.TargetId,
                notification.ReadAt != null,
                notification.ReadAt,
                notification.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            .CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, cancellationToken);

    public Task<bool> ExistsAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            .AnyAsync(notification => notification.Id == notificationId && notification.UserId == userId, cancellationToken);

    public Task<int> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            .Where(notification => notification.Id == notificationId
                                   && notification.UserId == userId
                                   && notification.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.ReadAt, now)
                .SetProperty(notification => notification.UpdatedAt, now), cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            .Where(notification => notification.UserId == userId
                                   && notification.ReadAt == null
                                   && notification.CreatedAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.ReadAt, now)
                .SetProperty(notification => notification.UpdatedAt, now), cancellationToken);
}
