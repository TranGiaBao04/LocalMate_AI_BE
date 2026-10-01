using System.Globalization;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

// Dựng mail biên nhận cho 1 đơn vừa thanh toán thành công (logic thuần, không chạm DB).
public static class PaymentReceiptEmailBuilder
{
    public static EmailOutboxEntry Build(User user, PaymentOrder order, SubscriptionPeriod period,
        SubscriptionPlan plan, SubscriptionPlanVersion version, DateTime paidAtUtc) =>
        BuildReceipt(user, order, plan.Name, version.DurationDays, period.EndsAt, paidAtUtc);

    public static EmailOutboxEntry Build(
        User user,
        PaymentOrder order,
        UserSubscription subscription,
        SubscriptionPlanDefinition plan,
        DateTime paidAtUtc)
    {
        return BuildReceipt(user, order, PlanName(order.PlanCode ?? PlanCode.Free),
            plan.DurationDays, subscription.EndsAt, paidAtUtc);
    }

    private static EmailOutboxEntry BuildReceipt(User user, PaymentOrder order, string planName,
        int? durationDays, DateTime endsAt, DateTime paidAtUtc)
    {
        var model = new PaymentReceiptEmailModel(
            user.FullName,
            order.ProviderOrderCode.ToString(CultureInfo.InvariantCulture),
            planName,
            order.Type == PaymentOrderType.Renewal ? "Gia hạn" : "Mua mới",
            EmailDisplayFormat.Money(order.Amount),
            EmailDisplayFormat.VietnamDateTime(paidAtUtc),
            $"{durationDays} ngày",
            EmailDisplayFormat.VietnamDateTime(endsAt));

        return new EmailOutboxEntry(
            user.Email,
            $"Biên nhận thanh toán gói {planName} - LocalMate AI",
            EmailTemplateNames.PaymentReceipt,
            EmailOutboxModelRegistry.Serialize(model),
            $"payment-receipt:{order.Id}");
    }

    public static string PlanName(PlanCode planCode) => planCode switch
    {
        PlanCode.TripPass => "Trip Pass",
        PlanCode.Membership => "Membership",
        _ => planCode.ToString()
    };
}
