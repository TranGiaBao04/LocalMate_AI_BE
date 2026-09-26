using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class PaymentService(
    IUserRepository userRepository,
    ISubscriptionRepository subscriptionRepository,
    IPaymentOrderRepository paymentOrderRepository,
    IPaymentOperationExecutor paymentOperationExecutor,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider) : IPaymentService
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
        // S2 exposes local order state only. S3 will reconcile Pending with the provider.
        return order is null
            ? new PaymentOrderLookupResult(PaymentOrderLookupStatus.NotFound)
            : new PaymentOrderLookupResult(
                PaymentOrderLookupStatus.Success,
                ToOrderResponse(order));
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
                // S2 expires locally; S3 must reconcile with the provider before finalizing this state.
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
