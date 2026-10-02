using System.Globalization;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

public static class UpgradePaymentReceiptEmailBuilder
{
    public static EmailOutboxEntry Build(User user, PaymentOrder order, SubscriptionPeriod period,
        SubscriptionPlan plan, SubscriptionPlanVersion version)
    {
        var model = new UpgradePaymentReceiptEmailModel(user.FullName,
            order.ProviderOrderCode.ToString(CultureInfo.InvariantCulture), plan.Name, "Nâng cấp",
            EmailDisplayFormat.Money(order.Amount + order.CreditAmount),
            EmailDisplayFormat.Money(order.CreditAmount), EmailDisplayFormat.Money(order.Amount),
            EmailDisplayFormat.VietnamDateTime(order.PaidAt!.Value), $"{version.DurationDays} ngày",
            EmailDisplayFormat.VietnamDateTime(period.StartsAt), EmailDisplayFormat.VietnamDateTime(period.EndsAt));
        return new(user.Email, $"Biên nhận nâng cấp gói {plan.Name} - LocalMate AI",
            EmailTemplateNames.UpgradePaymentReceipt, EmailOutboxModelRegistry.Serialize(model),
            $"payment-receipt:{order.Id}");
    }
}
