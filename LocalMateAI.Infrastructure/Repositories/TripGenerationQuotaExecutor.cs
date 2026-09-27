using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class TripGenerationQuotaExecutor(AppDbContext dbContext) : ITripGenerationQuotaExecutor
{
    public async Task<TripGenerationQuotaExecution<T>> ExecuteForUserAsync<T>(
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
            return new TripGenerationQuotaExecution<T>(false, default);
        }

        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new TripGenerationQuotaExecution<T>(true, result);
    }
}
