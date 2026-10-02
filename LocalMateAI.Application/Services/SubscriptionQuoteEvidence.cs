using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Domain.Services;

namespace LocalMateAI.Application.Services;

public static class SubscriptionQuoteEvidence
{
    public static IReadOnlyList<UpgradeCreditSource> Build(
        Guid userId, int targetPriority, DateTime nowUtc, IReadOnlyList<SubscriptionQuoteSource> evidence) =>
        evidence.Where(s => s.Period.UserId == userId && s.Plan.Id == s.Period.PlanId
            && s.Plan.Code != PlanIdentity.Free && s.Plan.EntitlementPriority < targetPriority
            && s.Period.TerminatedAt is null
            && SubscriptionPeriodLifecycle.EffectiveEnd(s.Period) > nowUtc)
        .Select(s =>
        {
            var eligible = HasMonetaryProvenance(s, userId);
            return new UpgradeCreditSource(s.Period.Id, s.Period.StartsAt, s.Period.EndsAt,
                s.Period.TerminatedAt, eligible ? s.PurchasedVersion!.Price : 0,
                eligible ? s.PurchasedVersion!.DurationDays!.Value : HistoricalDuration(s), eligible);
        }).ToArray();

    private static bool HasMonetaryProvenance(SubscriptionQuoteSource s, Guid userId)
    {
        var p = s.Period;
        var o = s.SourceOrder;
        var v = s.PurchasedVersion;
        if (p.LegacyUserSubscriptionId is not null || p.SourcePaymentOrderId is null
            || o is null || v is null || o.Id != p.SourcePaymentOrderId || o.UserId != userId
            || o.ProductKind != PaymentProductKind.SubscriptionPlan || o.Status != PaymentOrderStatus.Paid
            || o.PaidAt is null || o.PlanVersionBinding != PlanVersionBinding.Native
            || o.PlanId != p.PlanId || o.PlanVersionId != p.PlanVersionId
            || v.Id != p.PlanVersionId || v.PlanId != p.PlanId || v.Price <= 0
            || decimal.Truncate(v.Price) != v.Price || v.DurationDays is not > 0
            || o.Amount <= 0 || o.CreditAmount < 0
            || decimal.Truncate(o.Amount) != o.Amount || decimal.Truncate(o.CreditAmount) != o.CreditAmount)
            return false;
        return o.Type switch
        {
            PaymentOrderType.Purchase or PaymentOrderType.Renewal =>
                o.CreditAmount == 0 && o.Amount == v.Price,
            PaymentOrderType.Upgrade => o.Amount + o.CreditAmount == v.Price,
            _ => false
        };
    }

    // Count Vietnam calendar dates touched by the persisted half-open interval.
    // This is lifecycle display only; it cannot establish a legacy purchase price.
    private static int HistoricalDuration(SubscriptionQuoteSource s)
    {
        var start = DateOnly.FromDateTime(s.Period.StartsAt.AddHours(7));
        var end = DateOnly.FromDateTime(s.Period.EndsAt.AddTicks(-1).AddHours(7));
        return Math.Max(1, end.DayNumber - start.DayNumber + 1);
    }
}
