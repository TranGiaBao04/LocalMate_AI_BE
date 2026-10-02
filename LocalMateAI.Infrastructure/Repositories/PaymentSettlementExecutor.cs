using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentSettlementExecutor(AppDbContext dbContext, TimeProvider? timeProvider = null)
    : IPaymentSettlementExecutor
{
    public Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(
        long providerOrderCode,
        Func<PaymentOrder, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) => ExecuteAsync(providerOrderCode,
            new(PaymentStatusChangeSource.ProviderLookup), operation, cancellationToken);

    public async Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(long providerOrderCode,
        PaymentTransitionContext context, Func<PaymentOrder, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var userId = await dbContext.PaymentOrders
            .AsNoTracking()
            .Where(order => order.ProviderOrderCode == providerOrderCode)
            .Select(order => (Guid?)order.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (userId is null)
        {
            return new PaymentSettlementExecution<T>(false, default);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lockedUser = await dbContext.Database.SqlQuery<int>(
                $"""SELECT 1 AS "Value" FROM "Users" WHERE "Id" = {userId.Value} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (lockedUser != 1)
        {
            return new PaymentSettlementExecution<T>(false, default);
        }

        var lockedOrder = await dbContext.Database.SqlQuery<int>(
                $"""SELECT 1 AS "Value" FROM "PaymentOrders" WHERE "ProviderOrderCode" = {providerOrderCode} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (lockedOrder != 1)
        {
            return new PaymentSettlementExecution<T>(false, default);
        }

        var order = await dbContext.PaymentOrders.SingleAsync(
            candidate => candidate.ProviderOrderCode == providerOrderCode,
            cancellationToken);
        await dbContext.Entry(order).ReloadAsync(cancellationToken);
        var before = order.Status;
        var isUpgrade = order.ProductKind == PaymentProductKind.SubscriptionPlan
            && order.Type == PaymentOrderType.Upgrade && typeof(T) == typeof(PaymentSettlementResult);
        if (isUpgrade) await transaction.CreateSavepointAsync("upgrade_grant", cancellationToken);
        var result = await operation(order, cancellationToken);
        if (order.Status != before)
            dbContext.PaymentOrderStatusHistories.Add(context.History(order, before,
                (result as PaymentSettlementResult)?.OccurredAt ?? (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
                (result as PaymentSettlementResult)?.TransitionReasonCode));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (isUpgrade && order.Status == PaymentOrderStatus.Paid
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation,
                ConstraintName: "EX_Periods_UserPlan_NoOverlap" })
        {
            // An out-of-band target insert can win after the overlap read. Undo the entire grant,
            // but retain the earlier User/Order locks while recording durable financial review.
            await transaction.RollbackToSavepointAsync("upgrade_grant", cancellationToken);
            dbContext.ChangeTracker.Clear();
            order = await dbContext.PaymentOrders.SingleAsync(o => o.ProviderOrderCode == providerOrderCode, cancellationToken);
            order.Status = PaymentOrderStatus.ReviewRequired;
            var occurredAt = (result as PaymentSettlementResult)!.OccurredAt!.Value;
            var conflict = new PaymentSettlementResult(PaymentSettlementStatus.CreditConflict)
            { OccurredAt = occurredAt, TransitionReasonCode = "upgrade_credit_conflict" };
            dbContext.PaymentOrderStatusHistories.Add(context.History(order, before, occurredAt, conflict.TransitionReasonCode));
            await dbContext.SaveChangesAsync(cancellationToken);
            result = (T)(object)conflict;
        }
        await transaction.CommitAsync(cancellationToken);

        return new PaymentSettlementExecution<T>(true, result);
    }
}
