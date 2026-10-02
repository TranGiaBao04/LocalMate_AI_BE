using System.Globalization;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

public static class SingleItineraryReceiptEmailBuilder
{
    public static EmailOutboxEntry Build(User user, PaymentOrder order, DateTime paidAt) => new(
        user.Email, "Biên nhận thanh toán một lịch trình - LocalMate AI", EmailTemplateNames.SingleItineraryPaymentReceipt,
        EmailOutboxModelRegistry.Serialize(new SingleItineraryPaymentReceiptEmailModel(user.FullName,
            order.ProviderOrderCode.ToString(CultureInfo.InvariantCulture),
            EmailDisplayFormat.Money(order.Amount), EmailDisplayFormat.VietnamDateTime(paidAt))),
        $"payment-receipt:{order.Id}");
}
