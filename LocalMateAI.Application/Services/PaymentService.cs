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
    IPaymentReconciliationService reconciliation,
    TimeProvider timeProvider,
    ILogger<PaymentService> logger) : IPaymentService
{
    private static readonly TimeSpan PaymentIntentLifetime = TimeSpan.FromMinutes(15);

    public async Task<CheckoutQuoteResult> GetCheckoutQuoteAsync(
        Guid userId, string? planCode, CancellationToken cancellationToken = default)
    {
        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
            return new(PaymentIntentResultStatus.NonPersistedUser);
        var target = await GetTargetAsync(planCode, cancellationToken);
        if (target is null) return new(PaymentIntentResultStatus.InvalidPlanCode);
        var (plan, version) = target.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var classification = await ClassifyAsync(userId, plan, nowUtc, cancellationToken);
        if (ClassificationError(classification) is { } error) return new(error);
        if (classification == SubscriptionCheckoutClassification.Purchase)
            return new(PaymentIntentResultStatus.Success,
                new(PlanIdentity.PublicCode(plan.Code), "Purchase", version.Price, 0,
                    version.Price, version.DurationDays!.Value, []));

        var evidence = await subscriptionRepository.GetQuoteSourcesAsync(userId, cancellationToken);
        var sources = SubscriptionQuoteEvidence.Build(userId, plan.EntitlementPriority, nowUtc, evidence);
        var calculated = UpgradeCreditCalculator.Calculate(new(version.Price, nowUtc, sources));
        var byPeriod = evidence.ToDictionary(s => s.Period.Id);
        return new(PaymentIntentResultStatus.Success,
            new(PlanIdentity.PublicCode(plan.Code), "Upgrade", calculated.ListPrice,
                calculated.AppliedCredit, calculated.AmountPayable, version.DurationDays!.Value,
                calculated.Sources.Select(row => new CheckoutQuoteCreditResponse(
                    PlanIdentity.PublicCode(byPeriod[row.PeriodId].Plan.Code),
                    byPeriod[row.PeriodId].Plan.Name, row.RemainingDays, row.CalculatedCreditAmount)).ToArray()));
    }

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
        if (order is null || order.ProductKind != PaymentProductKind.SubscriptionPlan)
        {
            return new PaymentOrderLookupResult(PaymentOrderLookupStatus.NotFound);
        }

        if (HasUsablePaymentLink(order))
        {
            var reconciled = await reconciliation.ReconcileAsync(order.Id,
                new(PaymentStatusChangeSource.ProviderLookup, userId), cancellationToken);
            logger.LogDebug("Owned payment lookup {OrderId}: {ReconciliationResult}.", order.Id, reconciled.Status);
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
            ToOrderResponse(order, order.PlanId is { } planId
                ? (await subscriptionRepository.GetPlanAsync(planId, cancellationToken))?.Code : null));
    }

    private async Task<PaymentIntentResult> CheckoutLockedAsync(
        Guid userId,
        string? planCode,
        CancellationToken cancellationToken)
    {
        var target = await GetTargetAsync(planCode, cancellationToken);
        if (target is null) return new(PaymentIntentResultStatus.InvalidPlanCode);
        var (requestedPlan, version) = target.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var classification = await ClassifyAsync(userId, requestedPlan, nowUtc, cancellationToken);
        if (ClassificationError(classification) is { } error) return new(error);
        var type = classification == SubscriptionCheckoutClassification.Upgrade ? PaymentOrderType.Upgrade : PaymentOrderType.Purchase;
        if (await SubscriptionPendingOrderPolicy.CheckAsync(paymentOrderRepository, userId, requestedPlan.Id,
                type, nowUtc, cancellationToken) is { } blocker) return blocker;
        // Phase safety gate: UP-S3 reservation and UP-S4 settlement must precede a payable Upgrade.
        if (classification == SubscriptionCheckoutClassification.Upgrade)
            return new(PaymentIntentResultStatus.UpgradeCheckoutNotReady);

        return await CreatePaymentIntentAsync(
            userId,
            requestedPlan,
            version,
            PaymentOrderType.Purchase,
            nowUtc,
            cancellationToken);
    }

    private async Task<PaymentIntentResult> RenewLockedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var effective = await EffectiveSubscriptionResolver.ResolveAsync(subscriptionRepository, userId, nowUtc, cancellationToken);
        if (effective.Period is null || !effective.Plan.IsActive)
        {
            return new PaymentIntentResult(PaymentIntentResultStatus.NoActiveSubscription);
        }

        var target = await GetTargetAsync(effective.Plan.Code, cancellationToken);
        if (target is null) return new(PaymentIntentResultStatus.InvalidPlanCode);
        return await CreatePaymentIntentAsync(
            userId,
            target.Value.Plan,
            target.Value.Version,
            PaymentOrderType.Renewal,
            nowUtc,
            cancellationToken);
    }

    private async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        Guid userId,
        SubscriptionPlan plan,
        SubscriptionPlanVersion version,
        PaymentOrderType type,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (await SubscriptionPendingOrderPolicy.CheckAsync(paymentOrderRepository, userId, plan.Id,
                type, nowUtc, cancellationToken) is { } blocker) return blocker;

        var order = new PaymentOrder
        {
            UserId = userId,
            PlanCode = PlanIdentity.Legacy(plan.Code),
            PlanId = plan.Id,
            PlanVersionId = version.Id,
            PlanVersionBinding = PlanVersionBinding.Native,
            Type = type,
            Amount = version.Price,
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
                    PlanIdentity.PublicCode(plan.Code),
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
            await paymentOrderRepository.TransitionStatusAsync(order, PaymentOrderStatus.Failed,
                new(PaymentStatusChangeSource.Checkout, userId,
                    ReasonCode: link.IsSuccess ? "unusable_payment_link" : "gateway_unavailable"), nowUtc, cancellationToken);
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
        new(order.Id, order.QrCode!, order.CheckoutUrl!, order.Amount, order.ExpiresAt)
        { Type = order.Type.ToString(), ListPrice = order.Amount + order.CreditAmount, CreditAmount = order.CreditAmount };

    private static PaymentOrderResponse ToOrderResponse(PaymentOrder order, string? code) =>
        new(
            order.Id,
            order.Status.ToString(),
            code is null ? order.PlanCode?.ToString() ?? "" : PlanIdentity.PublicCode(code),
            order.Amount,
            order.ExpiresAt,
            order.PaidAt)
        { Type = order.Type.ToString(), ListPrice = order.Amount + order.CreditAmount, CreditAmount = order.CreditAmount };

    private async Task<(SubscriptionPlan Plan, SubscriptionPlanVersion Version)?> GetTargetAsync(
        string? planCode, CancellationToken cancellationToken)
    {
        return await SubscriptionCheckoutContext.GetTargetAsync(subscriptionRepository, planCode, cancellationToken);
    }

    private async Task<SubscriptionCheckoutClassification> ClassifyAsync(
        Guid userId, SubscriptionPlan target, DateTime nowUtc, CancellationToken cancellationToken)
    {
        return await SubscriptionCheckoutContext.ClassifyAsync(subscriptionRepository, userId, target, nowUtc, cancellationToken);
    }

    private static PaymentIntentResultStatus? ClassificationError(SubscriptionCheckoutClassification classification) =>
        SubscriptionCheckoutContext.ClassificationError(classification);
}
