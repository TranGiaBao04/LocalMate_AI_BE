using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class PaymentSettlementService(
    IPaymentSettlementExecutor settlementExecutor,
    ISubscriptionRepository subscriptionRepository,
    TimeProvider timeProvider,
    ILogger<PaymentSettlementService> logger) : IPaymentSettlementService
{
    public async Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default)
    {
        var execution = await settlementExecutor.ExecuteAsync(
            notification.ProviderOrderCode,
            (order, lockedCancellationToken) => SettleLockedAsync(
                order,
                notification,
                lockedCancellationToken),
            cancellationToken);

        return execution.OrderExists
            ? execution.Result!
            : new PaymentSettlementResult(PaymentSettlementStatus.UnknownOrder);
    }

    private async Task<PaymentSettlementResult> SettleLockedAsync(
        PaymentOrder order,
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken)
    {
        if (order.Status == PaymentOrderStatus.Paid)
        {
            return new PaymentSettlementResult(PaymentSettlementStatus.AlreadyPaid);
        }

        if (!notification.IsSuccessful)
        {
            order.Status = PaymentOrderStatus.Failed;
            return new PaymentSettlementResult(PaymentSettlementStatus.NonSuccessful);
        }

        if (order.Amount != notification.Amount)
        {
            order.Status = PaymentOrderStatus.Failed;
            logger.LogWarning(
                "Payment amount mismatch for provider order {ProviderOrderCode}: expected {ExpectedAmount}, received {ReceivedAmount}",
                order.ProviderOrderCode,
                order.Amount,
                notification.Amount);
            return new PaymentSettlementResult(PaymentSettlementStatus.AmountMismatch);
        }

        var plan = SubscriptionCatalog.Get(order.PlanCode);
        if (order.PlanCode == PlanCode.Free || plan.DurationDays is null)
        {
            order.Status = PaymentOrderStatus.Failed;
            logger.LogWarning(
                "Rejected paid settlement for non-paid plan on provider order {ProviderOrderCode}",
                order.ProviderOrderCode);
            return new PaymentSettlementResult(PaymentSettlementStatus.InvalidPaidPlan);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var subscription = await subscriptionRepository.GetByUserAndPlanAsync(
            order.UserId,
            order.PlanCode,
            cancellationToken);
        var duration = TimeSpan.FromDays(plan.DurationDays.Value);

        if (subscription is null)
        {
            subscription = new UserSubscription
            {
                UserId = order.UserId,
                PlanCode = order.PlanCode,
                StartsAt = nowUtc,
                EndsAt = nowUtc.Add(duration)
            };
            await subscriptionRepository.AddAsync(subscription, cancellationToken);
        }
        else if (subscription.EndsAt <= nowUtc)
        {
            subscription.StartsAt = nowUtc;
            subscription.EndsAt = nowUtc.Add(duration);
        }
        else
        {
            subscription.EndsAt = subscription.EndsAt.Add(duration);
        }

        order.Status = PaymentOrderStatus.Paid;
        order.PaidAt = nowUtc;
        return new PaymentSettlementResult(PaymentSettlementStatus.Settled);
    }
}
