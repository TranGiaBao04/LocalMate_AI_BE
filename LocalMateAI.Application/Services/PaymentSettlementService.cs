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
    IUserRepository userRepository,
    IEmailOutboxRepository emailOutboxRepository,
    TimeProvider timeProvider,
    ILogger<PaymentSettlementService> logger,
    ISingleItineraryRepository? singleRepository = null) : IPaymentSettlementService
{
    public Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default) => ApplyVerifiedPaymentAsync(notification,
            new(PaymentStatusChangeSource.ProviderLookup), cancellationToken);

    public async Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(VerifiedPaymentNotification notification,
        PaymentTransitionContext context, CancellationToken cancellationToken = default)
    {
        var execution = await settlementExecutor.ExecuteAsync(
            notification.ProviderOrderCode,
            context,
            (order, lockedCancellationToken) => SettleLockedAsync(
                order,
                notification,
                context,
                lockedCancellationToken),
            cancellationToken);

        return execution.OrderExists
            ? execution.Result!
            : new PaymentSettlementResult(PaymentSettlementStatus.UnknownOrder);
    }

    private async Task<PaymentSettlementResult> SettleLockedAsync(
        PaymentOrder order,
        VerifiedPaymentNotification notification,
        PaymentTransitionContext context,
        CancellationToken cancellationToken)
    {
        if (order.Status == PaymentOrderStatus.Paid)
        {
            return new PaymentSettlementResult(PaymentSettlementStatus.AlreadyPaid);
        }

        // Foundation only: no Upgrade grant before reservation/replacement settlement is implemented.
        if (order.Type == PaymentOrderType.Upgrade)
            return new PaymentSettlementResult(PaymentSettlementStatus.InvalidPaidPlan);

        if (!notification.IsSuccessful)
        {
            order.Status = PaymentOrderStatus.Failed;
            return new PaymentSettlementResult(PaymentSettlementStatus.NonSuccessful)
            { TransitionReasonCode = context.ReasonCode ?? "verified_non_success" };
        }

        if (order.Amount != notification.Amount)
        {
            order.Status = PaymentOrderStatus.Failed;
            logger.LogWarning(
                "Payment amount mismatch for provider order {ProviderOrderCode}: expected {ExpectedAmount}, received {ReceivedAmount}",
                order.ProviderOrderCode,
                order.Amount,
                notification.Amount);
            return new PaymentSettlementResult(PaymentSettlementStatus.AmountMismatch)
            { TransitionReasonCode = "amount_mismatch" };
        }

        if (order.ProductKind == PaymentProductKind.SingleItinerary)
            return await SettleSingleAsync(order, context, cancellationToken);

        if (order.PlanVersionBinding == PlanVersionBinding.LegacyUnresolved)
        {
            logger.LogError("Settlement blocked: order {OrderId} requires an approved historical version binding.", order.Id);
            return new PaymentSettlementResult(PaymentSettlementStatus.UnresolvedPlanVersion);
        }
        var plan = order.PlanId is { } planId
            ? await subscriptionRepository.GetPlanAsync(planId, cancellationToken) : null;
        var version = order.PlanVersionId is { } versionId
            ? await subscriptionRepository.GetVersionAsync(versionId, cancellationToken) : null;
        if (plan is null || version is null || version.PlanId != plan.Id
            || plan.Code == PlanIdentity.Free || version.DurationDays is null
            || version.Price != order.Amount)
        {
            logger.LogWarning(
                "Rejected paid settlement for non-paid plan on provider order {ProviderOrderCode}",
                order.ProviderOrderCode);
            return new PaymentSettlementResult(PaymentSettlementStatus.InvalidPaidPlan);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var periods = await subscriptionRepository.GetPeriodsAsync(order.UserId, cancellationToken);
        if (periods.Any(p => p.SourcePaymentOrderId == order.Id))
            return new PaymentSettlementResult(PaymentSettlementStatus.AlreadyPaid);
        var tail = periods.Where(p => p.PlanId == plan.Id).Select(p => p.EndsAt)
            .DefaultIfEmpty(nowUtc).Max();
        var start = tail > nowUtc ? tail : nowUtc;
        var subscription = new SubscriptionPeriod
        {
            UserId = order.UserId,
            PlanId = plan.Id,
            PlanVersionId = version.Id,
            StartsAt = start,
            EndsAt = start.AddDays(version.DurationDays.Value),
            SourcePaymentOrderId = order.Id
        };
        await subscriptionRepository.AddPeriodAsync(subscription, cancellationToken);

        order.Status = PaymentOrderStatus.Paid;
        order.PaidAt = nowUtc;
        await EnqueueReceiptAsync(order, subscription, plan, version, nowUtc, cancellationToken);
        return new PaymentSettlementResult(PaymentSettlementStatus.Settled)
        { TransitionReasonCode = context.ReasonCode ?? "verified_success" };
    }

    private async Task<PaymentSettlementResult> SettleSingleAsync(PaymentOrder order,
        PaymentTransitionContext context, CancellationToken ct)
    {
        var version = singleRepository is not null && order.SingleItineraryProductVersionId is { } id
            ? await singleRepository.GetVersionAsync(id, ct) : null;
        if (version is null || version.Price <= 0 || version.Price != order.Amount
            || order.Type != PaymentOrderType.Purchase || order.PlanId is not null || order.PlanVersionId is not null
            || order.PlanCode is not null || order.PlanVersionBinding is not null)
            return new(PaymentSettlementStatus.InvalidPaidPlan);
        if (await singleRepository!.GetForOrderAsync(order.Id, ct) is not null)
            return new(PaymentSettlementStatus.AlreadyPaid);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await singleRepository.AddAsync(new SingleItineraryEntitlement
        {
            UserId = order.UserId, SourcePaymentOrderId = order.Id,
            SingleItineraryProductVersionId = version.Id, GrantedAt = now
        }, ct);
        order.Status = PaymentOrderStatus.Paid;
        order.PaidAt = now;
        var user = await userRepository.GetByIdAsync(order.UserId, ct)
            ?? throw new InvalidOperationException("Paid order owner is missing.");
        await emailOutboxRepository.EnqueueAsync(SingleItineraryReceiptEmailBuilder.Build(user, order, now), now, ct);
        return new(PaymentSettlementStatus.Settled)
        { TransitionReasonCode = context.ReasonCode ?? "verified_success" };
    }

    // Xếp biên nhận vào outbox trong CÙNG transaction thanh toán: commit thì chắc chắn có mail chờ gửi.
    private async Task EnqueueReceiptAsync(
        PaymentOrder order,
        SubscriptionPeriod subscription,
        SubscriptionPlan plan,
        SubscriptionPlanVersion version,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(order.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning(
                "Receipt email skipped for provider order {ProviderOrderCode}: user not found",
                order.ProviderOrderCode);
            return;
        }

        var entry = PaymentReceiptEmailBuilder.Build(user, order, subscription, plan, version, nowUtc);
        await emailOutboxRepository.EnqueueAsync(entry, nowUtc, cancellationToken);
    }
}
