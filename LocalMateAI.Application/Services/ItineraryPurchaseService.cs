using LocalMateAI.Application.DTOs.ItineraryPurchases;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class ItineraryPurchaseService(ISingleItineraryRepository single, IPaymentOrderRepository orders,
    IPaymentOperationExecutor reservationExecutor, IPaymentSettlementExecutor completionExecutor, IPaymentGateway gateway,
    IPaymentReconciliationService reconciliation, IUserRepository users, ISubscriptionRepository subscriptions,
    ITripRepository trips, TimeProvider time) : IItineraryPurchaseService
{
    public async Task<ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>> CheckoutAsync(
        Guid userId, Guid attemptId, CancellationToken ct = default)
    {
        if (attemptId == Guid.Empty) return new(ItineraryPurchaseStatus.InvalidRequest);
        var reservation = await reservationExecutor.ExecuteForUserAsync(userId, async token =>
        {
            var previous = await single.GetAttemptAsync(userId, attemptId, token);
            if (previous is not null) return (Order: previous, Created: false);
            var now = time.GetUtcNow().UtcDateTime;
            var version = await single.GetCurrentVersionAsync(now, token);
            if (version is null) return (Order: (PaymentOrder?)null, Created: false);
            var order = new PaymentOrder
            {
                UserId = userId, ProductKind = PaymentProductKind.SingleItinerary, Type = PaymentOrderType.Purchase,
                PlanVersionBinding = null, SingleItineraryProductVersionId = version.Id,
                CheckoutAttemptId = attemptId, Amount = version.Price,
                Status = PaymentOrderStatus.Pending, ExpiresAt = now.AddMinutes(15)
            };
            await orders.AddAsync(order, token);
            return (Order: (PaymentOrder?)order, Created: true);
        }, ct);
        if (!reservation.PersistedUserExists) return new(ItineraryPurchaseStatus.AccountRequired);
        var reserved = reservation.Result.Order;
        if (reserved is null) return new(ItineraryPurchaseStatus.ProviderUnavailable);
        if (!reservation.Result.Created)
            return new(reserved.Status == PaymentOrderStatus.Pending && reserved.CheckoutUrl is null
                ? ItineraryPurchaseStatus.Accepted : ItineraryPurchaseStatus.Success, await ResponseAsync(reserved, ct));

        // The durable attempt owns one provider code. Network work is outside the reservation transaction.
        // Retries never create another link/charge, including an uncertain response or process interruption.
        PaymentLinkResult link;
        try
        {
            link = await gateway.CreatePaymentLinkAsync(new(reserved.Id, reserved.ProviderOrderCode, null,
                PaymentOrderType.Purchase, reserved.Amount, reserved.ExpiresAt), ct);
        }
        catch (Exception e) when (e is PaymentGatewayUnavailableException or HttpRequestException or TimeoutException
            || e is OperationCanceledException && !ct.IsCancellationRequested)
        { link = PaymentLinkResult.Unavailable(); }

        var usable = link.IsSuccess && !string.IsNullOrWhiteSpace(link.CheckoutUrl) && !string.IsNullOrWhiteSpace(link.QrCode);
        await completionExecutor.ExecuteAsync(reserved.ProviderOrderCode,
            new(PaymentStatusChangeSource.Checkout, userId, ReasonCode: "payment_link_unavailable"), (order, _) =>
        {
            if (order.Status == PaymentOrderStatus.Pending)
            {
                if (usable) { order.CheckoutUrl = link.CheckoutUrl; order.QrCode = link.QrCode; }
                else order.Status = PaymentOrderStatus.Failed;
            }
            return Task.FromResult(true);
        }, ct);
        var current = await orders.GetOwnedByIdAsync(reserved.Id, userId, ct) ?? throw new InvalidOperationException("Reserved order disappeared.");
        return new(usable || current.Status == PaymentOrderStatus.Paid ? ItineraryPurchaseStatus.Success
            : ItineraryPurchaseStatus.ProviderUnavailable, await ResponseAsync(current, ct));
    }

    public async Task<ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        if (await users.GetByIdAsync(userId, ct) is null) return new(ItineraryPurchaseStatus.AccountRequired);
        var order = await orders.GetOwnedByIdAsync(orderId, userId, ct);
        if (order is null || order.ProductKind != PaymentProductKind.SingleItinerary) return new(ItineraryPurchaseStatus.NotFound);
        if (order.Status != PaymentOrderStatus.Paid)
        {
            await reconciliation.ReconcileAsync(order.Id, new(PaymentStatusChangeSource.ProviderLookup, userId), ct);
            order = (await orders.GetOwnedByIdAsync(orderId, userId, ct))!;
        }
        return new(ItineraryPurchaseStatus.Success, await ResponseAsync(order, ct));
    }

    public async Task<ItineraryPurchaseResult<ItineraryPurchasesMeResponse>> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        if (await users.GetByIdAsync(userId, ct) is null) return new(ItineraryPurchaseStatus.AccountRequired);
        var rows = await single.GetOwnedAsync(userId, ct);
        return new(ItineraryPurchaseStatus.Success, new(rows.Count(e => e.ConsumedAt is null),
            rows.Select(SingleItineraryEntitlementResponse.From).ToArray()));
    }

    public async Task<ItineraryPurchaseResult<ItineraryPurchaseAvailabilityResponse>> GetAvailabilityAsync(Guid userId, CancellationToken ct = default)
    {
        if (await users.GetByIdAsync(userId, ct) is null) return new(ItineraryPurchaseStatus.AccountRequired);
        var now = time.GetUtcNow().UtcDateTime;
        var version = await single.GetCurrentVersionAsync(now, ct);
        if (version is null) return new(ItineraryPurchaseStatus.ProviderUnavailable);
        var effective = await EffectiveSubscriptionResolver.ResolveAsync(subscriptions, userId, now, ct);
        var used = await trips.CountNormalFinalizedByUserAsync(userId, ct);
        var owned = await single.GetOwnedAsync(userId, ct);
        var limit = effective.Version.SavedTripLimit;
        return new(ItineraryPurchaseStatus.Success, new("SingleItinerary", SingleItineraryProductVersion.ProductCode,
            "Một lịch trình bổ sung", version.Id, version.Price, "VND", true, null,
            owned.Count(e => e.ConsumedAt is null), limit is null || used < limit, used, limit));
    }

    private async Task<ItineraryPurchaseOrderResponse> ResponseAsync(PaymentOrder order, CancellationToken ct)
    {
        var grant = await single.GetForOrderAsync(order.Id, ct);
        return new(order.Id, "SingleItinerary", SingleItineraryProductVersion.ProductCode,
            order.SingleItineraryProductVersionId!.Value, order.Status.ToString(), order.Amount, "VND",
            order.ExpiresAt, order.PaidAt, order.Status == PaymentOrderStatus.Pending ? order.CheckoutUrl : null,
            order.Status == PaymentOrderStatus.Pending ? order.QrCode : null,
            grant is null ? null : SingleItineraryEntitlementResponse.From(grant));
    }
}
