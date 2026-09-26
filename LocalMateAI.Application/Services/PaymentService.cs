using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class PaymentService(
    IUserRepository userRepository,
    ISubscriptionRepository subscriptionRepository,
    IPaymentOrderRepository paymentOrderRepository,
    IPaymentOperationExecutor paymentOperationExecutor,
    IPaymentGateway paymentGateway,
    IPaymentSettlementService paymentSettlementService,
    TimeProvider timeProvider,
    ILogger<PaymentService> logger) : IPaymentService
{
    private static readonly TimeSpan PaymentIntentLifetime = TimeSpan.FromMinutes(15);

    public async Task<PaymentIntentResult> CheckoutAsync(
        Guid userId,
        string? planCode,
        CancellationToken cancellationToken = default)
    {
        var execution = await paymentOperationExecutor.ExecuteForUserAsync(
            userId,
            lockedCancellationToken => CheckoutLockedAsync(
                userId,
                planCode,
                lockedCancellationToken),
            cancellationToken);

        return execution.PersistedUserExists
            ? execution.Result!
            : new PaymentIntentResult(PaymentIntentResultStatus.NonPersistedUser);
    }

    public async Task<PaymentIntentResult> RenewAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var execution = await paymentOperationExecutor.ExecuteForUserAsync(
            userId,
            lockedCancellationToken => RenewLockedAsync(userId, lockedCancellationToken),
            cancellationToken);

        return execution.PersistedUserExists
            ? execution.Result!
            : new PaymentIntentResult(PaymentIntentResultStatus.NonPersistedUser);
    }

    public async Task<PaymentOrderLookupResult> GetOrderAsync(
        Guid userId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
        {
            return new PaymentOrderLookupResult(PaymentOrderLookupStatus.NonPersistedUser);
        }

        var order = await paymentOrderRepository.GetOwnedByIdAsync(
            orderId,
            userId,
            cancellationToken);
        if (order is null)
        {
            return new PaymentOrderLookupResult(PaymentOrderLookupStatus.NotFound);
        }

        if (order.Status != PaymentOrderStatus.Paid && HasUsablePaymentLink(order))
        {
            await ReconcileOrderAsync(order, cancellationToken);
            order = await paymentOrderRepository.GetOwnedByIdAsync(
                orderId,
                userId,
                cancellationToken);
            if (order is null)
            {
                return new PaymentOrderLookupResult(PaymentOrderLookupStatus.NotFound);
            }
        }

        return new PaymentOrderLookupResult(
            PaymentOrderLookupStatus.Success,
            ToOrderResponse(order));
    }

    private async Task ReconcileOrderAsync(
        PaymentOrder order,
        CancellationToken cancellationToken)
    {
        PaymentGatewayOrderResult providerOrder;
        try
        {
            providerOrder = await paymentGateway.GetPaymentAsync(
                order.ProviderOrderCode,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is PaymentGatewayUnavailableException or TimeoutException)
        {
            return;
        }

        if (!providerOrder.IsAvailable)
        {
            return;
        }

        if (providerOrder.ProviderOrderCode != order.ProviderOrderCode)
        {
            logger.LogWarning(
                "Ignored payment lookup with mismatched provider order code for local order {OrderId}",
                order.Id);
            return;
        }

        switch (providerOrder.Status)
        {
            case PaymentGatewayOrderStatus.Paid:
                await paymentSettlementService.ApplyVerifiedPaymentAsync(
                    new VerifiedPaymentNotification(
                        providerOrder.ProviderOrderCode,
                        providerOrder.Amount,
                        IsSuccessful: true),
                    cancellationToken);
                break;
            case PaymentGatewayOrderStatus.Cancelled:
            case PaymentGatewayOrderStatus.Underpaid:
            case PaymentGatewayOrderStatus.Failed:
                await paymentSettlementService.ApplyVerifiedPaymentAsync(
                    new VerifiedPaymentNotification(
                        providerOrder.ProviderOrderCode,
                        providerOrder.Amount,
                        IsSuccessful: false),
                    cancellationToken);
                break;
            case PaymentGatewayOrderStatus.Expired:
                await paymentOrderRepository.MarkExpiredIfPendingAsync(
                    order.Id,
                    timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);
                break;
            case PaymentGatewayOrderStatus.Pending:
                if (order.Status == PaymentOrderStatus.Pending
                    && order.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    await paymentOrderRepository.MarkExpiredIfPendingAsync(
                        order.Id,
                        timeProvider.GetUtcNow().UtcDateTime,
                        cancellationToken);
                }
                break;
            case PaymentGatewayOrderStatus.Processing:
                break;
            case PaymentGatewayOrderStatus.Unknown:
                logger.LogWarning(
                    "Ignored unknown provider payment status for order {ProviderOrderCode}",
                    order.ProviderOrderCode);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(providerOrder.Status),
                    providerOrder.Status,
                    "Unsupported provider payment status.");
        }
    }

    private async Task<PaymentIntentResult> CheckoutLockedAsync(
        Guid userId,
        string? planCode,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PlanCode>(planCode, ignoreCase: false, out var requestedPlan)
            || requestedPlan == PlanCode.Free)
        {
            return new PaymentIntentResult(PaymentIntentResultStatus.InvalidPlanCode);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var subscriptions = await subscriptionRepository.GetByUserIdAsync(userId, cancellationToken);
        var effective = SubscriptionCatalog.ResolveEffectivePaid(subscriptions, nowUtc);

        if (effective?.PlanCode == requestedPlan)
        {
            return new PaymentIntentResult(PaymentIntentResultStatus.PlanAlreadyActive);
        }

        if (effective?.PlanCode == PlanCode.Membership && requestedPlan == PlanCode.TripPass)
        {
            return new PaymentIntentResult(PaymentIntentResultStatus.CoveredByHigherPlan);
        }

        return await CreatePaymentIntentAsync(
            userId,
            requestedPlan,
            PaymentOrderType.Purchase,
            nowUtc,
            cancellationToken);
    }

    private async Task<PaymentIntentResult> RenewLockedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var subscriptions = await subscriptionRepository.GetByUserIdAsync(userId, cancellationToken);
        var effective = SubscriptionCatalog.ResolveEffectivePaid(subscriptions, nowUtc);
        if (effective is null)
        {
            return new PaymentIntentResult(PaymentIntentResultStatus.NoActiveSubscription);
        }

        return await CreatePaymentIntentAsync(
            userId,
            effective.PlanCode,
            PaymentOrderType.Renewal,
            nowUtc,
            cancellationToken);
    }

    private async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        Guid userId,
        PlanCode planCode,
        PaymentOrderType type,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var pending = await paymentOrderRepository.GetPendingAsync(
            userId,
            planCode,
            type,
            cancellationToken);
        if (pending is not null)
        {
            if (pending.ExpiresAt <= nowUtc)
            {
                // A later verified webhook or owned-order lookup can still settle this order.
                pending.Status = PaymentOrderStatus.Expired;
                await paymentOrderRepository.SaveChangesAsync(cancellationToken);
            }
            else if (HasUsablePaymentLink(pending))
            {
                return new PaymentIntentResult(
                    PaymentIntentResultStatus.PendingOrderExists,
                    ToIntentResponse(pending));
            }
            else
            {
                pending.Status = PaymentOrderStatus.Failed;
                await paymentOrderRepository.SaveChangesAsync(cancellationToken);
            }
        }

        var plan = SubscriptionCatalog.Get(planCode);
        var order = new PaymentOrder
        {
            UserId = userId,
            PlanCode = planCode,
            Type = type,
            Amount = plan.Price,
            Status = PaymentOrderStatus.Pending,
            ExpiresAt = nowUtc.Add(PaymentIntentLifetime),
            PaidAt = null
        };
        await paymentOrderRepository.AddAsync(order, cancellationToken);

        PaymentLinkResult link;
        try
        {
            link = await paymentGateway.CreatePaymentLinkAsync(
                new PaymentLinkRequest(
                    order.Id,
                    order.ProviderOrderCode,
                    order.PlanCode.ToString(),
                    order.Type,
                    order.Amount,
                    order.ExpiresAt),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is PaymentGatewayUnavailableException or TimeoutException)
        {
            link = PaymentLinkResult.Unavailable();
        }

        if (!link.IsSuccess
            || string.IsNullOrWhiteSpace(link.CheckoutUrl)
            || string.IsNullOrWhiteSpace(link.QrCode))
        {
            order.Status = PaymentOrderStatus.Failed;
            await paymentOrderRepository.SaveChangesAsync(cancellationToken);
            return new PaymentIntentResult(PaymentIntentResultStatus.GatewayUnavailable);
        }

        order.CheckoutUrl = link.CheckoutUrl;
        order.QrCode = link.QrCode;
        await paymentOrderRepository.SaveChangesAsync(cancellationToken);

        return new PaymentIntentResult(
            PaymentIntentResultStatus.Success,
            ToIntentResponse(order));
    }

    private static bool HasUsablePaymentLink(PaymentOrder order) =>
        !string.IsNullOrWhiteSpace(order.CheckoutUrl)
        && !string.IsNullOrWhiteSpace(order.QrCode);

    private static PaymentIntentResponse ToIntentResponse(PaymentOrder order) =>
        new(order.Id, order.QrCode!, order.CheckoutUrl!, order.Amount, order.ExpiresAt);

    private static PaymentOrderResponse ToOrderResponse(PaymentOrder order) =>
        new(
            order.Id,
            order.Status.ToString(),
            order.PlanCode.ToString(),
            order.Amount,
            order.ExpiresAt,
            order.PaidAt);
}
