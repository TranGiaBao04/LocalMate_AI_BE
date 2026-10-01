using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LocalMateAI.Infrastructure.Payments;

public sealed class PaymentReconciliationLeaseProvider(AppDbContext context, ILogger<PaymentReconciliationLeaseProvider> logger)
    : IPaymentReconciliationLeaseProvider
{
    // Stable application-wide key, scoped by PostgreSQL database.
    private const long LeaseKey = 0x4C4D504159524543;

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        var source = (NpgsqlConnection)context.Database.GetDbConnection();
        var builder = new NpgsqlConnectionStringBuilder(source.ConnectionString)
        { Pooling = false, Multiplexing = false };
        // CloneWith retains authentication/SSL settings even after Npgsql hides the password.
        var connection = source.CloneWith(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LeaseKey);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
                return new Lease(connection, logger);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class Lease(NpgsqlConnection connection, ILogger logger) : IAsyncDisposable
    {
        private bool disposed;
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", LeaseKey);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (Exception e) { logger.LogWarning("Payment reconciliation lease release failed ({ErrorType}); closing session.", e.GetType().Name); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
