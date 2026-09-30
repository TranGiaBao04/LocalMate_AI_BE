using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminOperationExecutor(AppDbContext dbContext) : IAdminOperationExecutor
{
    public async Task<T> ExecuteExclusiveAsync<T>(
        string lockName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockName);
        ArgumentNullException.ThrowIfNull(operation);

        // Không commit thì transaction tự rollback khi dispose (có exception ở bất kỳ bước nào).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Khoá theo tên, giữ tới khi transaction kết thúc: request khác cùng lockName phải đợi.
        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockName}))",
            cancellationToken);

        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
