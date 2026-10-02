using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

internal static class EntitlementRepairEvidenceReader
{
    internal static async Task<EntitlementRepairEvidence?> ReadAsync(AppDbContext context, Guid orderId, CancellationToken ct)
    {
        var order = await context.PaymentOrders.AsNoTracking().Where(o => o.Id == orderId)
            .Select(o => new RepairOrderEvidence(o.Id, o.UserId, o.PlanId, o.PlanVersionId,
                o.PlanVersionBinding, o.Status, o.Amount, o.PaidAt) { ProductKind = o.ProductKind, Type = o.Type }).SingleOrDefaultAsync(ct);
        return order is null ? null : await ReadAsync(context, order, ct);
    }

    internal static async Task<EntitlementRepairEvidence> ReadAsync(AppDbContext context, RepairOrderEvidence order, CancellationToken ct)
    {
        var userExists = await context.Users.AnyAsync(u => u.Id == order.UserId, ct);
        if (order.ProductKind != PaymentProductKind.SubscriptionPlan)
            return new(userExists, new(order, null), [], []);
        var purchases = await (
            from o in context.PaymentOrders.AsNoTracking()
            where o.ProductKind == PaymentProductKind.SubscriptionPlan &&
                (o.Id == order.Id || (o.UserId == order.UserId && o.PlanId == order.PlanId && o.Status == PaymentOrderStatus.Paid))
            join v in context.SubscriptionPlanVersions.AsNoTracking() on o.PlanVersionId equals (Guid?)v.Id into versions
            from v in versions.DefaultIfEmpty()
            join p in context.SubscriptionPlans.AsNoTracking() on o.PlanId equals (Guid?)p.Id into plans
            from p in plans.DefaultIfEmpty()
            select new RepairPurchaseEvidence(
                new(o.Id, o.UserId, o.PlanId, o.PlanVersionId, o.PlanVersionBinding, o.Status, o.Amount, o.PaidAt) { Type = o.Type },
                v != null && p != null ? new RepairVersionEvidence(v.Id, v.PlanId, p.Code, v.Price, v.DurationDays) : null))
            .ToListAsync(ct);
        var periods = await context.SubscriptionPeriods.AsNoTracking()
            .Where(p => (p.UserId == order.UserId && p.PlanId == order.PlanId) || p.SourcePaymentOrderId == order.Id)
            .ToListAsync(ct);
        return new(userExists, purchases.Single(p => p.Order.Id == order.Id), purchases, periods);
    }
}
