using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Notifications;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface INotificationRepository
{
    // Trả false khi đã có thông báo cùng DeduplicationKey. Dùng được bên trong transaction đang mở.
    Task<bool> EnqueueAsync(
        NotificationEntry entry,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<PagedResult<NotificationResponse>> GetPagedAsync(
        Guid userId,
        NotificationQuery query,
        CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    // Trả số dòng vừa chuyển sang đã đọc (0 nếu không phải của người này hoặc đã đọc từ trước).
    Task<int> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        DateTime now,
        CancellationToken cancellationToken = default);

    // Chỉ đánh dấu các thông báo tạo trước hoặc đúng thời điểm now.
    Task<int> MarkAllReadAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default);
}
