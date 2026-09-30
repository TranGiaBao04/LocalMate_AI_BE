namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminOperationExecutor
{
    /// <summary>
    /// Chạy thao tác trong 1 transaction và giữ khoá tuần tự theo <paramref name="lockName"/> tới khi commit/rollback.
    /// Có exception ⇒ rollback toàn bộ.
    /// </summary>
    Task<T> ExecuteExclusiveAsync<T>(
        string lockName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
