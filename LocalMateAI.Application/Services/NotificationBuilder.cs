using System.Globalization;
using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

// Dựng nội dung thông báo (logic thuần, không chạm DB). Tiêu đề và nội dung được lưu cứng vào bảng Notifications.
public static class NotificationBuilder
{
    public static NotificationEntry Welcome(Guid userId) => new(
        userId,
        NotificationTypes.Welcome,
        "Chào mừng bạn đến với LocalMate AI",
        "Tạo lịch trình đầu tiên quanh tuyến Metro số 1 ngay nhé.",
        NotificationTargetType.CreateTrip,
        null,
        $"welcome:{userId}");

    // plannedStartAt là giờ Việt Nam lưu sẵn trên trip (không quy đổi múi giờ), giống mail lịch trình.
    public static NotificationEntry TripFinalized(Guid userId, Guid tripId, DateTime? plannedStartAt) => new(
        userId,
        NotificationTypes.TripFinalized,
        "Lịch trình đã được chốt",
        plannedStartAt is { } start
            ? $"Chuyến đi ngày {start.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} lúc {start.ToString("HH:mm", CultureInfo.InvariantCulture)} đã sẵn sàng."
            : "Chuyến đi của bạn đã sẵn sàng.",
        NotificationTargetType.Trip,
        tripId,
        $"trip-finalized:{tripId}");

    public static NotificationEntry SubscriptionPaid(Guid userId, Guid orderId, string planName, DateTime endsAtUtc) =>
        SubscriptionPayment(userId, orderId,
            $"Gói {planName} có hiệu lực đến {EmailDisplayFormat.VietnamDate(endsAtUtc)}.");

    public static NotificationEntry SubscriptionUpgraded(Guid userId, Guid orderId, string planName, DateTime endsAtUtc) =>
        SubscriptionPayment(userId, orderId,
            $"Đã nâng cấp lên gói {planName}, có hiệu lực đến {EmailDisplayFormat.VietnamDate(endsAtUtc)}.");

    // startsAtUtc muộn hơn lúc tặng khi gói tặng được xếp nối sau gói đang có.
    public static NotificationEntry PlanGranted(Guid userId, Guid grantId, string planName,
        DateTime startsAtUtc, DateTime endsAtUtc, DateTime nowUtc) => new(
        userId,
        NotificationTypes.PlanGranted,
        $"Bạn được tặng gói {planName}",
        startsAtUtc > nowUtc
            ? $"Gói {planName} có hiệu lực từ {EmailDisplayFormat.VietnamDate(startsAtUtc)} đến {EmailDisplayFormat.VietnamDate(endsAtUtc)}."
            : $"Gói {planName} có hiệu lực đến {EmailDisplayFormat.VietnamDate(endsAtUtc)}.",
        NotificationTargetType.Subscription,
        null,
        $"plan-grant:{grantId}");

    private static NotificationEntry SubscriptionPayment(Guid userId, Guid orderId, string body) => new(
        userId,
        NotificationTypes.SubscriptionPaymentSucceeded,
        "Thanh toán thành công",
        body,
        NotificationTargetType.Subscription,
        null,
        $"payment:{orderId}");
}
