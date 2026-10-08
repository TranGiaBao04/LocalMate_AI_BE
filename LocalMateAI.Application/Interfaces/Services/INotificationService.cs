using LocalMateAI.Application.DTOs.Notifications;

namespace LocalMateAI.Application.Interfaces.Services;

public interface INotificationService
{
    Task<NotificationListResult> GetNotificationsAsync(
        Guid userId,
        NotificationQuery query,
        CancellationToken cancellationToken = default);

    Task<UnreadNotificationCountResponse> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    // Trả false khi thông báo không tồn tại hoặc không phải của người này.
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
