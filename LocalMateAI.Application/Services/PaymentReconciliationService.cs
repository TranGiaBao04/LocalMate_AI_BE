using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class PaymentReconciliationService(IPaymentOrderRepository orders, IPaymentGateway gateway,
    IPaymentSettlementService settlement, TimeProvider clock, ILogger<PaymentReconciliationService> logger)
    : IPaymentReconciliationService
{
    public async Task<PaymentReconciliationResult> ReconcileAsync(Guid orderId, PaymentTransitionContext context,
        CancellationToken cancellationToken = default)
    {
        var order = await orders.GetByIdAsync(orderId, cancellationToken);
        var before = order?.Status;
        if (order is null) return Result(PaymentReconciliationStatus.NotFound, null, false, null);
        if (before == PaymentOrderStatus.Paid) return Result(PaymentReconciliationStatus.AlreadyPaid, before, false, null);

        PaymentGatewayOrderResult provider;
        try { provider = await gateway.GetPaymentAsync(order.ProviderOrderCode, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            logger.LogWarning("Payment reconciliation provider unavailable for {OrderId} ({ErrorType}).", orderId, e.GetType().Name);
            return await Finish(PaymentReconciliationStatus.ProviderUnavailable, null);
        }
        if (!provider.IsAvailable) return await Finish(PaymentReconciliationStatus.ProviderUnavailable, null);
        if (provider.ProviderOrderCode != order.ProviderOrderCode)
        {
            logger.LogWarning("Payment reconciliation provider code mismatch for {OrderId}.", orderId);
            return await Finish(PaymentReconciliationStatus.ProviderMismatch, null);
        }

        PaymentSettlementResult? settled = null;
        // All callers use this mapping and the existing atomic settlement/expiry paths.
        switch (provider.Status)
        {
            case PaymentGatewayOrderStatus.Paid:
                settled = await settlement.ApplyVerifiedPaymentAsync(new(provider.ProviderOrderCode, provider.Amount, true),
                    context with { ReasonCode = "provider_paid" }, cancellationToken);
                break;
            case PaymentGatewayOrderStatus.Cancelled:
            case PaymentGatewayOrderStatus.Underpaid:
            case PaymentGatewayOrderStatus.Failed:
                settled = await settlement.ApplyVerifiedPaymentAsync(new(provider.ProviderOrderCode, provider.Amount, false),
                    context with { ReasonCode = $"provider_{provider.Status.ToString().ToLowerInvariant()}" }, cancellationToken);
                break;
            case PaymentGatewayOrderStatus.Expired:
                await orders.MarkExpiredIfPendingAsync(orderId, clock.GetUtcNow().UtcDateTime,
                    context with { ReasonCode = "provider_expired" }, cancellationToken);
                break;
            case PaymentGatewayOrderStatus.Pending:
                if (before == PaymentOrderStatus.Pending && order.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
                    await orders.MarkExpiredIfPendingAsync(orderId, clock.GetUtcNow().UtcDateTime,
                        context with { ReasonCode = "local_expired" }, cancellationToken);
                break;
            case PaymentGatewayOrderStatus.Processing:
            case PaymentGatewayOrderStatus.Unknown:
            default:
                logger.LogInformation("Payment reconciliation has no transition for {OrderId}, provider {ProviderStatus}.", orderId, provider.Status);
                break;
        }
        var after = await orders.GetByIdAsync(orderId, cancellationToken);
        var status = after is null ? PaymentReconciliationStatus.NotFound
            : settled?.Status == PaymentSettlementStatus.AlreadyPaid ? PaymentReconciliationStatus.AlreadyPaid
            : before != after.Status ? PaymentReconciliationStatus.Reconciled : PaymentReconciliationStatus.NoChange;
        return Result(status, after?.Status, true, provider.Status, settled?.Status);

        PaymentReconciliationResult Result(PaymentReconciliationStatus status, PaymentOrderStatus? after,
            bool checkedProvider, PaymentGatewayOrderStatus? providerStatus, PaymentSettlementStatus? settlementStatus = null) =>
            new(status, orderId, order?.ProviderOrderCode, checkedProvider, providerStatus, before, after,
                before != after, clock.GetUtcNow().UtcDateTime, settlementStatus);

        async Task<PaymentReconciliationResult> Finish(PaymentReconciliationStatus status, PaymentGatewayOrderStatus? providerStatus)
        {
            logger.LogInformation("Payment reconciliation {Result} for {OrderId}; no local mutation requested.", status, orderId);
            var latest = await orders.GetByIdAsync(orderId, cancellationToken);
            return Result(latest is null ? PaymentReconciliationStatus.NotFound : status, latest?.Status, true, providerStatus);
        }
    }
}
