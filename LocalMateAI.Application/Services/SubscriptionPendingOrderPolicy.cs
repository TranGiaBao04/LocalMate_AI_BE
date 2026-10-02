using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public static class SubscriptionPendingOrderPolicy
{
    // Called only inside the User serialization transaction; never probes a provider.
    public static async Task<PaymentIntentResult?> CheckAsync(IPaymentOrderRepository repository,
        Guid userId, Guid planId, PaymentOrderType type, DateTime nowUtc, CancellationToken cancellationToken)
    {
        while (await repository.GetBlockingSubscriptionAsync(userId, cancellationToken) is { } order)
        {
            if (order.Type != PaymentOrderType.Upgrade && order.Status == PaymentOrderStatus.Pending)
            {
                if (order.ExpiresAt <= nowUtc)
                {
                    await repository.TransitionStatusAsync(order, PaymentOrderStatus.Expired,
                        new(PaymentStatusChangeSource.LocalExpiration, userId, ReasonCode: "local_expired"), nowUtc, cancellationToken);
                    continue;
                }
                if (order.PlanId == planId && order.Type == type
                    && (string.IsNullOrWhiteSpace(order.CheckoutUrl) || string.IsNullOrWhiteSpace(order.QrCode)))
                {
                    await repository.TransitionStatusAsync(order, PaymentOrderStatus.Failed,
                        new(PaymentStatusChangeSource.Checkout, userId, ReasonCode: "unusable_pending_payment_link"), nowUtc, cancellationToken);
                    continue;
                }
            }
            return new(order.PlanId == planId && order.Type == type && order.Status == PaymentOrderStatus.Pending
                    && order.ExpiresAt > nowUtc && !string.IsNullOrWhiteSpace(order.CheckoutUrl) && !string.IsNullOrWhiteSpace(order.QrCode)
                    ? PaymentIntentResultStatus.PendingOrderExists : PaymentIntentResultStatus.AnotherPendingOrder,
                new(order.Id, order.QrCode ?? "", order.CheckoutUrl ?? "", order.Amount, order.ExpiresAt)
                { Type = order.Type.ToString(), ListPrice = order.Amount + order.CreditAmount, CreditAmount = order.CreditAmount });
        }
        return null;
    }
}
