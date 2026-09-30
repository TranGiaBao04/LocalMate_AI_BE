using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
namespace LocalMateAI.Tests;

public sealed class IsolatedPlanDatabase : IAsyncDisposable
{
    private readonly string adminConnection;
    public string Connection { get; }
    private readonly string name;
    private IsolatedPlanDatabase(string admin, string connection, string database)
    { adminConnection = admin; Connection = connection; name = database; }
    public AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Connection, o => o.UseNetTopologySuite()).Options);
    public static async Task<IsolatedPlanDatabase> CreateAsync(bool previousSchema = false)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgresTestDatabase.RequireConnection());
        builder.Database = "postgres";
        builder.Pooling = false;
        var admin = builder.ConnectionString;
        var name = "localmate_s1b_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection).ExecuteNonQueryAsync();
        builder.Database = name;
        var result = new IsolatedPlanDatabase(admin, builder.ConnectionString, name);
        try
        {
            await using var context = result.Context();
            if (previousSchema)
                await context.GetService<IMigrator>().MigrateAsync("20260928050449_AddEmailOutbox");
            else await context.Database.MigrateAsync();
            return result;
        }
        catch { await result.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        // Only the unique database created by this instance can be removed.
        if (!name.StartsWith("localmate_s1b_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing unsafe database cleanup.");
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection).ExecuteNonQueryAsync();
    }
}
