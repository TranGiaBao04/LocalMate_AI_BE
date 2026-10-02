using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using LocalMateAI.Application.Payments;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentOrderRepository(AppDbContext dbContext, TimeProvider? timeProvider = null) : IPaymentOrderRepository
{
    public async Task<PaymentOrder?> GetBlockingSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var order = await dbContext.PaymentOrders.Where(o => o.UserId == userId && o.ProductKind == PaymentProductKind.SubscriptionPlan
            && (o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.ReviewRequired
                || (o.Type == PaymentOrderType.Upgrade && o.Status != PaymentOrderStatus.Paid
                && dbContext.PaymentOrderCredits.Any(c => c.OrderId == o.Id && c.ReleasedAt == null))))
            .OrderBy(o => o.Status != PaymentOrderStatus.ReviewRequired).ThenBy(o => o.CreatedAt)
            .ThenBy(o => o.Id).FirstOrDefaultAsync(cancellationToken);
        // Callers hold the User lock; refresh an entity retained across short orchestration transactions.
        if (order is not null) await dbContext.Entry(order).ReloadAsync(cancellationToken);
        return order;
    }

    public async Task<PaymentOrder?> CompleteUpgradeLinkAsync(Guid userId, Guid orderId, PaymentLinkResult link,
        DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await dbContext.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Users\" WHERE \"Id\"={userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) != 1) return null;
        await dbContext.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"PaymentOrders\" WHERE \"Id\"={orderId} AND \"UserId\"={userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var order = await dbContext.PaymentOrders.SingleOrDefaultAsync(o => o.Id == orderId && o.UserId == userId
            && o.ProductKind == PaymentProductKind.SubscriptionPlan && o.Type == PaymentOrderType.Upgrade, cancellationToken);
        if (order is null) return null;
        await dbContext.Entry(order).ReloadAsync(cancellationToken);
        if (order.Status == PaymentOrderStatus.Pending)
        {
            if (link.IsSuccess && !string.IsNullOrWhiteSpace(link.CheckoutUrl) && !string.IsNullOrWhiteSpace(link.QrCode)
                && order.ExpiresAt > nowUtc)
            {
                order.CheckoutUrl = link.CheckoutUrl;
                order.QrCode = link.QrCode;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
                await TransitionStatusAsync(order, PaymentOrderStatus.Failed,
                    new(PaymentStatusChangeSource.Checkout, userId,
                        ReasonCode: link.IsSuccess ? "unusable_payment_link" : "gateway_unavailable"), nowUtc, cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return order;
    }

    public Task<PaymentOrder?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        dbContext.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetExpiredPendingIdsAsync(DateTime nowUtc, int batchSize,
        CancellationToken cancellationToken = default) =>
        await dbContext.PaymentOrders.AsNoTracking().Where(o => o.Status == PaymentOrderStatus.Pending && o.ExpiresAt <= nowUtc)
            .OrderBy(o => o.ExpiresAt).ThenBy(o => o.Id).Select(o => o.Id).Take(batchSize).ToListAsync(cancellationToken);
    public Task<PaymentOrder?> GetPendingAsync(
        Guid userId,
        Guid planId,
        PaymentOrderType type,
        CancellationToken cancellationToken = default) =>
        dbContext.PaymentOrders
            .Where(order => order.UserId == userId
                            && order.PlanId == planId
                            && order.Type == type
                            && order.Status == PaymentOrderStatus.Pending)
            .OrderByDescending(order => order.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PaymentOrder?> GetOwnedByIdAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                order => order.Id == orderId && order.UserId == userId,
                cancellationToken);

    public Task<bool> MarkExpiredIfPendingAsync(
        Guid orderId,
        DateTime updatedAt,
        CancellationToken cancellationToken = default) => MarkExpiredIfPendingAsync(orderId, updatedAt,
            new(PaymentStatusChangeSource.LocalExpiration, ReasonCode: "local_expired"), cancellationToken);

    public async Task<bool> MarkExpiredIfPendingAsync(Guid orderId, DateTime nowUtc, PaymentTransitionContext context,
        CancellationToken cancellationToken = default)
    {
        var code = await dbContext.PaymentOrders.AsNoTracking().Where(o => o.Id == orderId)
            .Select(o => (long?)o.ProviderOrderCode).SingleOrDefaultAsync(cancellationToken);
        if (code is null) return false;
        var execution = await new PaymentSettlementExecutor(dbContext, timeProvider).ExecuteAsync(code.Value, context,
            (order, _) =>
            {
                if (order.Status != PaymentOrderStatus.Pending) return Task.FromResult(false);
                order.Status = PaymentOrderStatus.Expired;
                return Task.FromResult(true);
            }, cancellationToken);
        return execution.OrderExists && execution.Result;
    }

    public async Task TransitionStatusAsync(PaymentOrder order, PaymentOrderStatus status, PaymentTransitionContext context,
        DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (order.Status == status) return;
        var before = order.Status;
        order.Status = status;
        dbContext.PaymentOrderStatusHistories.Add(context.History(order, before, nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(
        PaymentOrder order,
        CancellationToken cancellationToken = default)
    {
        dbContext.PaymentOrders.Add(order);
        if (order.Status == PaymentOrderStatus.Pending && (order.PlanVersionBinding == PlanVersionBinding.Native
            || order.ProductKind == PaymentProductKind.SingleItinerary))
            dbContext.PaymentOrderStatusHistories.Add(new PaymentTransitionContext(PaymentStatusChangeSource.Checkout,
                order.UserId, ReasonCode: "order_created").History(order, null,
                (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
