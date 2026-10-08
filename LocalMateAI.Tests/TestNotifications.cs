using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Interfaces.Repositories;

namespace LocalMateAI.Tests;

/// <summary>Repository giả: ghi lại các thông báo được tạo; đặt ThrowOnEnqueue để giả lập DB lỗi.</summary>
internal sealed class RecordingNotificationRepository : INotificationRepository
{
    public List<(NotificationEntry Entry, DateTime Now)> Entries { get; } = [];
    public bool ThrowOnEnqueue { get; init; }

    public Task<bool> EnqueueAsync(NotificationEntry entry, DateTime now, CancellationToken cancellationToken = default)
    {
        if (ThrowOnEnqueue)
        {
            throw new InvalidOperationException("Notification store is unavailable.");
        }

        Entries.Add((entry, now));
        return Task.FromResult(true);
    }

    public Task<PagedResult<NotificationResponse>> GetPagedAsync(Guid userId, NotificationQuery query,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> ExistsAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<int> MarkReadAsync(Guid userId, Guid notificationId, DateTime now,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> MarkAllReadAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
