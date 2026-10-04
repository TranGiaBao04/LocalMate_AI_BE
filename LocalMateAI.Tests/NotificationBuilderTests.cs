using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class NotificationBuilderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Welcome_PointsToCreateTrip_AndIsUniquePerUser()
    {
        var entry = NotificationBuilder.Welcome(UserId);

        Assert.Equal(new NotificationEntry(
            UserId,
            NotificationTypes.Welcome,
            "Chào mừng bạn đến với LocalMate AI",
            "Tạo lịch trình đầu tiên quanh tuyến Metro số 1 ngay nhé.",
            NotificationTargetType.CreateTrip,
            null,
            $"welcome:{UserId}"), entry);
    }

    [Fact]
    public void TripFinalized_WithPlannedStart_ShowsStoredDateAndTimeWithoutConversion()
    {
        var tripId = Guid.NewGuid();

        var entry = NotificationBuilder.TripFinalized(UserId, tripId, new DateTime(2026, 10, 10, 9, 5, 0));

        Assert.Equal(NotificationTypes.TripFinalized, entry.Type);
        Assert.Equal("Lịch trình đã được chốt", entry.Title);
        Assert.Equal("Chuyến đi ngày 10/10/2026 lúc 09:05 đã sẵn sàng.", entry.Body);
        Assert.Equal(NotificationTargetType.Trip, entry.TargetType);
        Assert.Equal(tripId, entry.TargetId);
        Assert.Equal($"trip-finalized:{tripId}", entry.DeduplicationKey);
    }

    [Fact]
    public void TripFinalized_WithoutPlannedStart_UsesGenericBody()
    {
        var entry = NotificationBuilder.TripFinalized(UserId, Guid.NewGuid(), null);

        Assert.Equal("Chuyến đi của bạn đã sẵn sàng.", entry.Body);
    }

    [Fact]
    public void SubscriptionPaid_ShowsExpiryDateInVietnamTime()
    {
        var orderId = Guid.NewGuid();
        // 17:30 UTC ngày 11/10 là 00:30 ngày 12/10 theo giờ Việt Nam.
        var endsAt = new DateTime(2026, 10, 11, 17, 30, 0, DateTimeKind.Utc);

        var entry = NotificationBuilder.SubscriptionPaid(UserId, orderId, "Trip Pass", endsAt);

        Assert.Equal(NotificationTypes.SubscriptionPaymentSucceeded, entry.Type);
        Assert.Equal("Thanh toán thành công", entry.Title);
        Assert.Equal("Gói Trip Pass có hiệu lực đến 12/10/2026.", entry.Body);
        Assert.Equal(NotificationTargetType.Subscription, entry.TargetType);
        Assert.Null(entry.TargetId);
        Assert.Equal($"payment:{orderId}", entry.DeduplicationKey);
    }

    [Fact]
    public void SubscriptionUpgraded_SharesTypeAndKeyWithPayment_ButSaysUpgrade()
    {
        var orderId = Guid.NewGuid();
        var endsAt = new DateTime(2026, 11, 3, 4, 0, 0, DateTimeKind.Utc);

        var entry = NotificationBuilder.SubscriptionUpgraded(UserId, orderId, "Membership", endsAt);

        Assert.Equal(NotificationTypes.SubscriptionPaymentSucceeded, entry.Type);
        Assert.Equal("Đã nâng cấp lên gói Membership, có hiệu lực đến 03/11/2026.", entry.Body);
        Assert.Equal($"payment:{orderId}", entry.DeduplicationKey);
    }
}
