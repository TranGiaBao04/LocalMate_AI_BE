using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Domain.Services;

namespace LocalMateAI.Application.Services;

public static class EntitlementRepairAssessmentPolicy
{
    public const string ReplayMode = "DeterministicHistoricalReplay";
    public const string UpgradeReplayMode = "UpgradeDeterministicHistoricalReplay";

    public static EntitlementRepairAssessment Assess(EntitlementRepairEvidence evidence, DateTime nowUtc)
    {
        var target = evidence.Target.Order;
        EntitlementRepairAssessment Reject(string code, string message, bool conflict = false, string status = "Missing") =>
            new(new(conflict ? "Conflict" : status), new(false, code, message, null, null, null, nowUtc), conflict);
        EntitlementRepairAssessment Granted(SubscriptionPeriod p) => new(new("Granted", p.Id, p.StartsAt, p.EndsAt),
            new(false, "already_granted", "This order already has its entitlement.", null, null, null, nowUtc));

        if (target.ProductKind != PaymentProductKind.SubscriptionPlan)
            return Reject("product_not_applicable", "Subscription repair does not apply to this product.", status: "NotApplicable");
        if (target.Status == PaymentOrderStatus.ReviewRequired)
            return Reject("payment_review_required", "This payment requires manual financial review.", status: "Unknown");
        var linked = evidence.Periods.Where(p => p.SourcePaymentOrderId == target.Id).ToArray();
        if (target.Type == PaymentOrderType.Upgrade)
        {
            if (target.Status != PaymentOrderStatus.Paid)
                return Reject("order_not_paid", "Only paid orders can be repaired.", status: "NotApplicable");
            if (target.Binding != PlanVersionBinding.Native)
                return Reject("historical_binding_not_supported", "Historical bindings cannot be repaired automatically.", status: "Unknown");
            if (target.PaidAt is null) return Reject("paid_at_missing", "The original settlement time is missing.");
            if (!evidence.UserExists || !ValidVersion(evidence.Target) || target.PaidAt.Value.Kind != DateTimeKind.Utc)
                return Reject("invalid_purchased_version", "The purchased plan version is invalid.");
            if (!ValidFinancialProof(evidence.Target))
                return Reject("amount_mismatch", "The persisted cash and credit do not match the purchased version.");
            if (!UpgradeSourcesValid(evidence.Target, evidence))
                return Reject("upgrade_consumption_unproven", "The original credit consumption is incomplete or contradictory.", true);
            DateTime end;
            try { end = target.PaidAt.Value.AddDays(evidence.Target.Version!.DurationDays!.Value); }
            catch (ArgumentOutOfRangeException)
            { return Reject("historical_window_unproven", "The historical range cannot be reconstructed.", status: "Unknown"); }
            if (linked.Length > 0)
            {
                if (linked.Length != 1 || !MatchesPurchase(linked[0], evidence.Target)
                    || linked[0].StartsAt != target.PaidAt || linked[0].EndsAt != end
                    || !TerminationProven(linked[0], evidence))
                    return Reject("historical_replay_conflict", "Existing entitlement contradicts its purchase.", true);
                return Granted(linked[0]);
            }
            if (HasOverlap(evidence, target, target.PaidAt.Value, end))
                return Reject("historical_replay_conflict", "The target window conflicts with persisted entitlement.", true);
            return new(new("Missing"), new(true, "eligible_upgrade_historical_restore",
                "The exact Upgrade target period can be restored.", target.PaidAt, end, UpgradeReplayMode, nowUtc));
        }
        if (linked.Length > 0)
        {
            if (linked.Length != 1 || !evidence.UserExists || target.Status != PaymentOrderStatus.Paid
                || target.PaidAt is null || target.Binding == PlanVersionBinding.LegacyUnresolved
                || !ValidPurchase(evidence.Target) || !MatchesPurchase(linked[0], evidence.Target)
                || !TerminationProven(linked[0], evidence))
                return Reject("historical_replay_conflict", "Existing entitlement contradicts its purchase.", true);
            return Granted(linked[0]);
        }
        if (target.Status != PaymentOrderStatus.Paid)
            return Reject("order_not_paid", "Only paid orders can be repaired.", status: "NotApplicable");
        if (target.Binding != PlanVersionBinding.Native)
            return Reject("historical_binding_not_supported", "Historical bindings cannot be repaired automatically.", status: "Unknown");
        if (target.PaidAt is null) return Reject("paid_at_missing", "The original settlement time is missing.");
        if (evidence.Target.Version?.PlanCode == PlanIdentity.Free)
            return Reject("free_plan_not_repairable", "Free plans do not have purchased periods.", status: "NotApplicable");
        if (!evidence.UserExists || !ValidVersion(evidence.Target))
            return Reject("invalid_purchased_version", "The purchased plan version is invalid.");
        if (!ValidFinancialProof(evidence.Target))
            return Reject("amount_mismatch", "The order amount does not match its purchased version.");
        if (evidence.Periods.Any(p => p.UserId == target.UserId && p.PlanId == target.PlanId && p.LegacyUserSubscriptionId is not null))
            return Reject("legacy_entitlement_ambiguous", "A legacy aggregate prevents exact historical reconstruction.", status: "Unknown");

        var relevant = evidence.Purchases.Where(p => p.Order.UserId == target.UserId
            && p.Order.PlanId == target.PlanId && p.Order.Status == PaymentOrderStatus.Paid).ToArray();
        if (relevant.All(p => p.Order.Id != target.Id)
            || relevant.Any(p => p.Order.Binding != PlanVersionBinding.Native || p.Order.PaidAt is null
                || p.Order.PaidAt.Value.Kind != DateTimeKind.Utc || !ValidPurchase(p)
                || (p.Order.Type == PaymentOrderType.Upgrade && !UpgradeSourcesValid(p, evidence)))
            || relevant.GroupBy(p => p.Order.Id).Any(g => g.Count() != 1)
            || relevant.GroupBy(p => p.Order.PaidAt).Any(g => g.Count() > 1))
            return Reject("historical_window_unproven", "The historical sequence is incomplete or ambiguous.", status: "Unknown");

        DateTime? targetStart = null, targetEnd = null;
        var replayed = new List<SubscriptionPeriod>();
        foreach (var purchase in relevant.OrderBy(p => p.Order.PaidAt))
        {
            var at = purchase.Order.PaidAt!.Value;
            var tail = replayed.Where(p => p.StartsAt < HistoricalEnd(p, at))
                .Select(p => HistoricalEnd(p, at)).DefaultIfEmpty(at).Max();
            var start = purchase.Order.Type != PaymentOrderType.Upgrade && tail > at ? tail : at;
            DateTime end;
            try { end = start.AddDays(purchase.Version!.DurationDays!.Value); }
            catch (ArgumentOutOfRangeException)
            { return Reject("historical_window_unproven", "The historical range cannot be reconstructed.", status: "Unknown"); }
            var periods = evidence.Periods.Where(p => p.SourcePaymentOrderId == purchase.Order.Id).ToArray();
            if (periods.Length > 1 || periods.Any(p => !MatchesPurchase(p, purchase) || p.StartsAt != start || p.EndsAt != end
                || !TerminationProven(p, evidence)))
                return Reject("historical_replay_conflict", "Persisted entitlement contradicts historical replay.", true);
            if (purchase.Order.Id == target.Id) { targetStart = start; targetEnd = end; }
            replayed.Add(periods.Length == 1 ? periods[0] : new SubscriptionPeriod { StartsAt = start, EndsAt = end });
        }
        if (evidence.Periods.Any(p => p.UserId == target.UserId && p.PlanId == target.PlanId
            && relevant.All(o => o.Order.Id != p.SourcePaymentOrderId))
            || HasOverlap(evidence, target, targetStart!.Value, targetEnd!.Value))
            return Reject("historical_replay_conflict", "The reconstructed period conflicts with persisted entitlement.", true);
        return new(new("Missing"), new(true, "eligible_historical_restore", "The exact historical period can be restored.",
            targetStart, targetEnd, ReplayMode, nowUtc));
    }

    private static bool HasOverlap(EntitlementRepairEvidence evidence, RepairOrderEvidence target, DateTime start, DateTime end) =>
        evidence.Periods.Any(p => p.UserId == target.UserId && p.PlanId == target.PlanId
            && SubscriptionPeriodLifecycle.HasEffectiveDuration(p) && p.StartsAt < end
            && start < SubscriptionPeriodLifecycle.EffectiveEnd(p));

    // A later termination cannot retroactively change the tail used by an earlier renewal.
    private static DateTime HistoricalEnd(SubscriptionPeriod p, DateTime at) =>
        SubscriptionPeriodLifecycle.EffectiveEnd(p.EndsAt, p.TerminatedAt is { } t && t <= at ? t : null);

    private static bool ValidVersion(RepairPurchaseEvidence p) => p.Order.PlanId is { } planId
        && p.Order.PlanVersionId is { } versionId && p.Version is { } v && v.Id == versionId && v.PlanId == planId
        && v.PlanCode != PlanIdentity.Free && v.Price > 0 && decimal.Truncate(v.Price) == v.Price && v.DurationDays > 0;
    private static bool ValidFinancialProof(RepairPurchaseEvidence p) => p.Order.Amount > 0
        && decimal.Truncate(p.Order.Amount) == p.Order.Amount && p.Order.CreditAmount >= 0
        && decimal.Truncate(p.Order.CreditAmount) == p.Order.CreditAmount
        && (p.Order.Type == PaymentOrderType.Upgrade ? p.Order.Amount + p.Order.CreditAmount == p.Version!.Price
            : p.Order.Type is PaymentOrderType.Purchase or PaymentOrderType.Renewal
                && p.Order.CreditAmount == 0 && p.Order.Amount == p.Version!.Price);
    private static bool ValidPurchase(RepairPurchaseEvidence p) => ValidVersion(p) && ValidFinancialProof(p);
    private static bool MatchesPurchase(SubscriptionPeriod p, RepairPurchaseEvidence purchase) =>
        p.UserId == purchase.Order.UserId && p.PlanId == purchase.Order.PlanId && p.PlanVersionId == purchase.Order.PlanVersionId
        && p.SourcePaymentOrderId == purchase.Order.Id && p.LegacyUserSubscriptionId is null
        && p.StartsAt.Kind == DateTimeKind.Utc && p.EndsAt.Kind == DateTimeKind.Utc && p.StartsAt < p.EndsAt
        && (p.EndsAt.Ticks - p.StartsAt.Ticks) % TimeSpan.TicksPerDay == 0
        && (p.EndsAt.Ticks - p.StartsAt.Ticks) / TimeSpan.TicksPerDay == purchase.Version!.DurationDays;

    private static bool UpgradeSourcesValid(RepairPurchaseEvidence purchase, EntitlementRepairEvidence evidence)
    {
        var o = purchase.Order;
        if (o.ProductKind != PaymentProductKind.SubscriptionPlan || o.Type != PaymentOrderType.Upgrade
            || o.Status != PaymentOrderStatus.Paid || o.Binding != PlanVersionBinding.Native
            || o.PaidAt is not { Kind: DateTimeKind.Utc } || !ValidPurchase(purchase)) return false;
        var claims = evidence.Claims.Where(c => c.OrderId == o.Id).ToArray();
        return claims.Length > 0 && claims.Select(c => c.PeriodId).Distinct().Count() == claims.Length
            && claims.All(c => c.UserId == o.UserId && c.ReleasedAt is null && !c.HasAnyReleaseEvidence()
                && c.RemainingDays >= 0 && c.CalculatedCreditAmount >= 0
                && decimal.Truncate(c.CalculatedCreditAmount) == c.CalculatedCreditAmount
                && evidence.Periods.Count(p => p.Id == c.PeriodId) == 1
                && evidence.Periods.Any(p => p.Id == c.PeriodId && p.UserId == o.UserId
                    && p.EndsAt == c.OriginalEndsAt && p.TerminatedByOrderId == o.Id && p.TerminatedAt == o.PaidAt));
    }

    private static bool TerminationProven(SubscriptionPeriod period, EntitlementRepairEvidence evidence)
    {
        if (period.TerminatedAt is null) return period.TerminatedByOrderId is null;
        var orders = evidence.Purchases.Where(p => p.Order.Id == period.TerminatedByOrderId).ToArray();
        return orders.Length == 1 && orders[0].Order.UserId == period.UserId
            && orders[0].Order.PaidAt == period.TerminatedAt && UpgradeSourcesValid(orders[0], evidence)
            && evidence.Claims.Any(c => c.OrderId == orders[0].Order.Id && c.PeriodId == period.Id);
    }
}
