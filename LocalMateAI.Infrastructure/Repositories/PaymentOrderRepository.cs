using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using LocalMateAI.Application.Payments;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentOrderRepository(AppDbContext dbContext, TimeProvider? timeProvider = null) : IPaymentOrderRepository
{
    public Task<PaymentOrder?> GetBlockingSubscriptionAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.PaymentOrders.Where(o => o.UserId == userId && o.ProductKind == PaymentProductKind.SubscriptionPlan
            && (o.Status == PaymentOrderStatus.Pending || (o.Type == PaymentOrderType.Upgrade
                && dbContext.PaymentOrderCredits.Any(c => c.OrderId == o.Id && c.ReleasedAt == null))))
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).FirstOrDefaultAsync(cancellationToken);

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
