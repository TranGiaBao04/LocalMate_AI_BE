using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentSettlementExecutor(AppDbContext dbContext)
    : IPaymentSettlementExecutor
{
    public async Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(
        long providerOrderCode,
        Func<PaymentOrder, CancellationToken, Task<T>> operation,
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
        var result = await operation(order, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PaymentSettlementExecution<T>(true, result);
    }
}
