using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentOperationExecutor(AppDbContext dbContext) : IPaymentOperationExecutor
{
    public async Task<PaymentOperationExecution<T>> ExecuteForUserAsync<T>(
        Guid userId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lockedUser = await dbContext.Database.SqlQuery<int>(
                $"""SELECT 1 AS "Value" FROM "Users" WHERE "Id" = {userId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (lockedUser != 1)
        {
            return new PaymentOperationExecution<T>(false, default);
        }

        // Phase 1 keeps this lock through gateway link creation so another checkout
        // cannot observe or create a half-initialized Pending order for the same user.
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PaymentOperationExecution<T>(true, result);
    }
}
