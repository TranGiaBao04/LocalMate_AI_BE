using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public static class EntitlementRepairAssessmentPolicy
{
    public const string ReplayMode = "DeterministicHistoricalReplay";

    public static EntitlementRepairAssessment Assess(EntitlementRepairEvidence evidence, DateTime nowUtc)
    {
        var target = evidence.Target.Order;
        var linked = evidence.Periods.Where(p => p.SourcePaymentOrderId == target.Id).ToArray();
        if (linked.Length > 0)
        {
            if (linked.Length != 1 || !evidence.UserExists || target.Status != PaymentOrderStatus.Paid
                || target.PaidAt is null || target.Binding == PlanVersionBinding.LegacyUnresolved
                || !ValidPurchase(evidence.Target) || !MatchesPurchase(linked[0], evidence.Target))
                return Reject("historical_replay_conflict", "Existing entitlement contradicts its purchase.", true);
            var p = linked[0];
            return new(new("Granted", p.Id, p.StartsAt, p.EndsAt),
                new(false, "already_granted", "This order already has its entitlement.", null, null, null, nowUtc));
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
        if (target.Amount != evidence.Target.Version!.Price)
            return Reject("amount_mismatch", "The order amount does not match its purchased version.");
        if (evidence.Periods.Any(p => p.UserId == target.UserId && p.PlanId == target.PlanId && p.LegacyUserSubscriptionId is not null))
            return Reject("legacy_entitlement_ambiguous", "A legacy aggregate prevents exact historical reconstruction.", status: "Unknown");

        var relevant = evidence.Purchases.Where(p => p.Order.UserId == target.UserId
            && p.Order.PlanId == target.PlanId && p.Order.Status == PaymentOrderStatus.Paid).ToArray();
        if (relevant.All(p => p.Order.Id != target.Id)
            || relevant.Any(p => p.Order.Binding != PlanVersionBinding.Native || p.Order.PaidAt is null
                || p.Order.PaidAt.Value.Kind != DateTimeKind.Utc || !ValidPurchase(p))
            || relevant.GroupBy(p => p.Order.Id).Any(g => g.Count() != 1)
            || relevant.GroupBy(p => p.Order.PaidAt).Any(g => g.Count() > 1))
            return Reject("historical_window_unproven", "The historical sequence is incomplete or ambiguous.", status: "Unknown");

        DateTime? tail = null, targetStart = null, targetEnd = null;
        foreach (var purchase in relevant.OrderBy(p => p.Order.PaidAt))
        {
            var start = tail is { } previous && previous > purchase.Order.PaidAt!.Value
                ? previous : purchase.Order.PaidAt!.Value;
            DateTime end;
            try { end = start.AddDays(purchase.Version!.DurationDays!.Value); }
            catch (ArgumentOutOfRangeException)
            { return Reject("historical_window_unproven", "The historical range cannot be reconstructed.", status: "Unknown"); }
            var periods = evidence.Periods.Where(p => p.SourcePaymentOrderId == purchase.Order.Id).ToArray();
            if (periods.Length > 1 || periods.Any(p => !MatchesPurchase(p, purchase) || p.StartsAt != start || p.EndsAt != end))
                return Reject("historical_replay_conflict", "Persisted entitlement contradicts historical replay.", true);
            if (purchase.Order.Id == target.Id) { targetStart = start; targetEnd = end; }
            tail = end;
        }
        if (evidence.Periods.Any(p => p.UserId == target.UserId && p.PlanId == target.PlanId
            && (relevant.All(o => o.Order.Id != p.SourcePaymentOrderId)
                || (p.StartsAt < targetEnd!.Value && targetStart!.Value < p.EndsAt))))
            return Reject("historical_replay_conflict", "The reconstructed period conflicts with persisted entitlement.", true);
        return new(new("Missing"), new(true, "eligible_historical_restore", "The exact historical period can be restored.",
            targetStart, targetEnd, ReplayMode, nowUtc));

        EntitlementRepairAssessment Reject(string code, string message, bool conflict = false, string status = "Missing") =>
            new(new(conflict ? "Conflict" : status), new(false, code, message, null, null, null, nowUtc), conflict);
    }

    private static bool ValidVersion(RepairPurchaseEvidence p) => p.Order.PlanId is { } planId
        && p.Order.PlanVersionId is { } versionId && p.Version is { } v && v.Id == versionId && v.PlanId == planId
        && v.PlanCode != PlanIdentity.Free && v.Price > 0 && decimal.Truncate(v.Price) == v.Price && v.DurationDays > 0;
    private static bool ValidPurchase(RepairPurchaseEvidence p) => ValidVersion(p) && p.Order.Amount == p.Version!.Price;
    private static bool MatchesPurchase(SubscriptionPeriod p, RepairPurchaseEvidence purchase) =>
        p.UserId == purchase.Order.UserId && p.PlanId == purchase.Order.PlanId && p.PlanVersionId == purchase.Order.PlanVersionId
        && p.SourcePaymentOrderId == purchase.Order.Id && p.LegacyUserSubscriptionId is null
        && p.StartsAt < p.EndsAt && (p.EndsAt.Ticks - p.StartsAt.Ticks) % TimeSpan.TicksPerDay == 0
        && (p.EndsAt.Ticks - p.StartsAt.Ticks) / TimeSpan.TicksPerDay == purchase.Version!.DurationDays;
}
