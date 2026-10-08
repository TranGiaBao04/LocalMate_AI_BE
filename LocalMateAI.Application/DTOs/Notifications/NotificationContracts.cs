using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Notifications;

public static class NotificationTypes
{
    public const string Welcome = "welcome";
    public const string TripFinalized = "trip_finalized";
    public const string SubscriptionPaymentSucceeded = "subscription_payment_succeeded";
}

// Một thông báo cần tạo. DeduplicationKey trùng thì lần tạo sau bị bỏ qua.
public sealed record NotificationEntry(
    Guid UserId,
    string Type,
    string Title,
    string Body,
    NotificationTargetType TargetType,
    Guid? TargetId,
    string DeduplicationKey);

public sealed record NotificationQuery : PagedQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = ["createdAt"];

    public bool UnreadOnly { get; init; }
}

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Body,
    NotificationTargetType TargetType,
    Guid? TargetId,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt);

public sealed record UnreadNotificationCountResponse(int Count);

public enum NotificationListResultStatus
{
    Success,
    InvalidQuery
}

public sealed record NotificationListResult(
    NotificationListResultStatus Status,
    PagedResult<NotificationResponse>? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
