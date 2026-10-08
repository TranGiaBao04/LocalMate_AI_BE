using FluentValidation;
using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class NotificationService(
    INotificationRepository repository,
    IValidator<NotificationQuery> validator,
    TimeProvider timeProvider) : INotificationService
{
    public async Task<NotificationListResult> GetNotificationsAsync(
        Guid userId,
        NotificationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return new NotificationListResult(NotificationListResultStatus.InvalidQuery, ValidationErrors: validation.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));
        }

        var page = await repository.GetPagedAsync(userId, query, cancellationToken);
        return new NotificationListResult(NotificationListResultStatus.Success, page);
    }

    public async Task<UnreadNotificationCountResponse> GetUnreadCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        new(await repository.CountUnreadAsync(userId, cancellationToken));

    public async Task<bool> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await repository.MarkReadAsync(userId, notificationId, now, cancellationToken);

        // 0 dòng: hoặc đã đọc từ trước (vẫn coi là thành công), hoặc không phải thông báo của người này.
        return updated > 0 || await repository.ExistsAsync(userId, notificationId, cancellationToken);
    }

    public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        repository.MarkAllReadAsync(userId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
}
