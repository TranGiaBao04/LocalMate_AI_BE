using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace LocalMateAI.Tests;

public static class PostgresTestDatabase
{
    public static string RequireConnection()
    {
        var connection = Environment.GetEnvironmentVariable("LOCALMATE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("PostgreSQL tests require LOCALMATE_TEST_CONNECTION (isolated test DB).");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (!parsed.Database!.StartsWith("localmate_s1b_test", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing non-isolated PostgreSQL test database.");
        return connection;
    }
    public static async Task ImportLegacyAsync(AppDbContext context, Guid userId)
    {
        foreach (var s in await context.UserSubscriptions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync())
            context.SubscriptionPeriods.Add(new SubscriptionPeriod
            {
                UserId = s.UserId,
                PlanId = SubscriptionBaseline.PlanId(s.PlanCode),
                PlanVersionId = SubscriptionBaseline.VersionId(s.PlanCode),
                StartsAt = s.StartsAt,
                EndsAt = s.EndsAt,
                LegacyUserSubscriptionId = s.Id
            });
        await context.SaveChangesAsync();
    }
}
