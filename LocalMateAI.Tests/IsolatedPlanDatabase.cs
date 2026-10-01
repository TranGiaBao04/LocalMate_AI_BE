using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Domain.Entities;
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
    internal AppDbContext ContextBeforeSingleItinerary() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Connection, o => o.UseNetTopologySuite())
        .ReplaceService<IModelCustomizer, BeforeSingleItineraryModelCustomizer>().Options);
    public static async Task<IsolatedPlanDatabase> CreateAsync(bool previousSchema = false, string? targetMigration = null)
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
            if (targetMigration is not null)
                await context.GetService<IMigrator>().MigrateAsync(targetMigration);
            else if (previousSchema)
                await context.GetService<IMigrator>().MigrateAsync("20260930043206_AddRbacAndUserStatus");
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

// Historical upgrade fixtures must not select/write columns introduced by a later migration.
internal sealed class BeforeSingleItineraryModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Ignore<SingleItineraryEntitlement>();
        modelBuilder.Ignore<SingleItineraryProductVersion>();
        var order = modelBuilder.Entity<PaymentOrder>();
        order.Ignore(o => o.ProductKind);
        order.Ignore(o => o.CheckoutAttemptId);
        order.Ignore(o => o.SingleItineraryProductVersionId);
    }
}
