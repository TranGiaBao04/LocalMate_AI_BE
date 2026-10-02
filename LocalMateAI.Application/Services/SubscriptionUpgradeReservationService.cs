using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class SubscriptionUpgradeReservationService(ISubscriptionRepository subscriptions,
    IPaymentOrderRepository orders, IPaymentCreditRepository credits, IPaymentOperationExecutor executor,
    IPaymentGateway gateway, TimeProvider timeProvider) : ISubscriptionUpgradeReservationService
{
    public async Task<UpgradePreparationResult> PrepareAsync(Guid userId, string planCode, CancellationToken cancellationToken = default)
    {
        var execution = await executor.ExecuteForUserAsync(userId, async ct =>
        {
            var target = await SubscriptionCheckoutContext.GetTargetAsync(subscriptions, planCode, ct);
            if (target is null) return new UpgradePreparationResult(PaymentIntentResultStatus.InvalidPlanCode);
            var (plan, version) = target.Value;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (await orders.GetBlockingSubscriptionAsync(userId, ct) is { Status: PaymentOrderStatus.ReviewRequired } review)
                return new(PaymentIntentResultStatus.PaymentReviewRequired, review.Id);
            var classification = await SubscriptionCheckoutContext.ClassifyAsync(subscriptions, userId, plan, now, ct);
            if (SubscriptionCheckoutContext.ClassificationError(classification) is { } error) return new(error);
            if (classification != SubscriptionCheckoutClassification.Upgrade) return new(PaymentIntentResultStatus.InvalidPlanCode);
            if (await SubscriptionPendingOrderPolicy.CheckAsync(orders, userId, plan.Id, PaymentOrderType.Upgrade, now, ct) is { } blocker)
                return new(blocker.Status, blocker.Response?.OrderId);
            var evidence = await subscriptions.GetQuoteSourcesAsync(userId, ct);
            var sources = SubscriptionQuoteEvidence.Build(userId, plan.EntitlementPriority, now, evidence);
            var calculated = UpgradeCreditCalculator.Calculate(new(version.Price, now, sources));
            var byPeriod = evidence.ToDictionary(s => s.Period.Id);
            var order = new PaymentOrder
            {
                UserId = userId, ProductKind = PaymentProductKind.SubscriptionPlan, Type = PaymentOrderType.Upgrade,
                PlanCode = PlanIdentity.Legacy(plan.Code), PlanId = plan.Id, PlanVersionId = version.Id,
                PlanVersionBinding = PlanVersionBinding.Native, Amount = calculated.AmountPayable,
                CreditAmount = calculated.AppliedCredit, Status = PaymentOrderStatus.Pending, ExpiresAt = now.AddMinutes(15)
            };
            await orders.AddAsync(order, ct);
            await credits.AddClaimsAsync(calculated.Sources.Select(s => new PaymentOrderCredit
            {
                OrderId = order.Id, UserId = userId, PeriodId = s.PeriodId,
                OriginalEndsAt = byPeriod[s.PeriodId].Period.EndsAt, RemainingDays = s.RemainingDays,
                CalculatedCreditAmount = s.CalculatedCreditAmount
            }).ToArray(), ct);
            return new(PaymentIntentResultStatus.Success, order.Id);
        }, cancellationToken);
        return execution.PersistedUserExists ? execution.Result! : new(PaymentIntentResultStatus.NonPersistedUser);
    }

    public async Task<UpgradeReleaseResult> ResolveForReplacementAsync(Guid userId, Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var candidate = await credits.GetReleaseCandidateAsync(userId, orderId, cancellationToken);
        if (candidate is null) return new(UpgradeReleaseStatus.NotFound, orderId);
        if (candidate.Claims.Count == 0 || candidate.Claims.All(c => c.ReleasedAt is not null))
            return new(UpgradeReleaseStatus.AlreadyReleased, orderId);
        if (candidate.Status == PaymentOrderStatus.Paid) return new(UpgradeReleaseStatus.RequiresSettlement, orderId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (candidate.Status == PaymentOrderStatus.Pending)
        {
            if (candidate.ExpiresAt > now) return new(UpgradeReleaseStatus.Blocked, orderId);
            // Expiry and its real history commit independently. Expiry is never release proof.
            await orders.MarkExpiredIfPendingAsync(orderId, now, cancellationToken);
            candidate = await credits.GetReleaseCandidateAsync(userId, orderId, cancellationToken);
            if (candidate is null) return new(UpgradeReleaseStatus.NotFound, orderId);
        }
        if (candidate.Status is not (PaymentOrderStatus.Failed or PaymentOrderStatus.Expired))
            return new(UpgradeReleaseStatus.Blocked, orderId);

        PaymentGatewayOrderResult provider;
        try { provider = await gateway.GetPaymentAsync(candidate.ProviderOrderCode, cancellationToken); }
        catch (Exception ex) when (ex is PaymentGatewayUnavailableException or TimeoutException or HttpRequestException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        { return new(UpgradeReleaseStatus.Blocked, orderId); }
        // No User/Order transaction spans this provider lookup.
        if (provider.IsAvailable && provider.ProviderOrderCode == candidate.ProviderOrderCode
            && provider.Status == PaymentGatewayOrderStatus.Paid)
            return new(UpgradeReleaseStatus.RequiresSettlement, orderId)
            { VerifiedPayment = new(provider.ProviderOrderCode, provider.Amount, true) };
        var proof = UpgradeReleasePolicy.Assess(candidate, provider, timeProvider.GetUtcNow().UtcDateTime);
        if (proof is null) return new(UpgradeReleaseStatus.Blocked, orderId);
        return new(await credits.ReleaseAsync(candidate, proof, cancellationToken), orderId);
    }
}
