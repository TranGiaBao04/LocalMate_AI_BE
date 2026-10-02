using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Payments;

public sealed record UpgradePreparationResult(PaymentIntentResultStatus Status, Guid? OrderId = null);
public enum UpgradeReleaseStatus { Released, AlreadyReleased, Blocked, RequiresSettlement, NotFound, StaleEvidence }
public sealed record UpgradeReleaseResult(UpgradeReleaseStatus Status, Guid OrderId)
{
    public VerifiedPaymentNotification? VerifiedPayment { get; init; }
}
public sealed record UpgradeSettlementSource(PaymentOrderCredit Claim, SubscriptionPeriod? Period);
public sealed record CreditClaimSnapshot(Guid PeriodId, Guid UserId, DateTime OriginalEndsAt,
    int RemainingDays, decimal CalculatedCreditAmount, DateTime? ReleasedAt,
    DateTime SourceEndsAt, DateTime? SourceTerminatedAt, Guid? SourceTerminatedByOrderId);
public sealed record UpgradeReleaseCandidate(Guid OrderId, Guid UserId, long ProviderOrderCode,
    PaymentOrderStatus Status, decimal Amount, decimal CreditAmount, DateTime? PaidAt, DateTime UpdatedAt,
    DateTime ExpiresAt, string? CheckoutUrl, string? QrCode, IReadOnlyList<CreditClaimSnapshot> Claims);

public static class UpgradeReleasePolicy
{
    public static CreditReleaseEvidence? Assess(UpgradeReleaseCandidate order, PaymentGatewayOrderResult provider, DateTime checkedAt)
    {
        if (!provider.IsAvailable || provider.ProviderOrderCode != order.ProviderOrderCode
            || provider.Status != PaymentGatewayOrderStatus.Cancelled || provider.RequestedAmount != order.Amount
            || provider.AmountPaid != 0 || provider.AmountRemaining != order.Amount) return null;
        return new(checkedAt, "Cancelled", order.Amount, 0, order.Amount, CreditReleaseEvidence.SafeReason);
    }
}
