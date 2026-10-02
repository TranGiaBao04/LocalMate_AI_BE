using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LocalMateAI.Tests;

public sealed class EntitlementRepairDetailPostgresTests
{
    [Fact]
    public async Task PreviewAndMutation_ShareReplayThenRefresh_OrderedSafeAudit_NoUsageRewrite()
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context();
        var trip = new Trip { UserId = f.Orders[0].UserId };
        c.Trips.Add(trip); await c.SaveChangesAsync();
        c.UsageEvents.Add(new UsageEvent { UserId = trip.UserId!.Value, TripId = trip.Id, Type = UsageEventType.Generate });
        await c.SaveChangesAsync();
        var usage = JsonSerializer.Serialize(await c.UsageEvents.AsNoTracking().ToArrayAsync());
        var repo = new AdminTransactionRepository(c);
        var preview = (await repo.GetDetailAsync(f.Orders[0].Id))!;
        Assert.True(preview.RepairEligibility!.Eligible);
        Assert.Equal("Missing", preview.Entitlement!.GrantStatus);
        Assert.Empty(preview.RepairHistory);
        using var host = new AdminTransactionsHttpTests.Host(repair: EntitlementRepairPostgresFixture.Repair(c));
        host.Authenticate("manage");
        var response = await host.Client.PostAsJsonAsync($"/api/admin/transactions/{f.Orders[0].Id}/repair-entitlement", new { reason = "recovery reviewed" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(preview.RepairEligibility.ProposedStartsAt, json.RootElement.GetProperty("startsAt").GetDateTime());
        Assert.Equal(preview.RepairEligibility.ProposedEndsAt, json.RootElement.GetProperty("endsAt").GetDateTime());
        await EntitlementRepairPostgresFixture.Repair(c).RepairAsync(f.Orders[0].Id, f.Actor, "second attempt");
        c.ChangeTracker.Clear();
        var refreshed = (await repo.GetDetailAsync(f.Orders[0].Id))!;
        Assert.Equal("Granted", refreshed.Entitlement!.GrantStatus);
        Assert.Equal("already_granted", refreshed.RepairEligibility!.Code);
        Assert.False(refreshed.RepairEligibility.Eligible);
        Assert.Equal(2, refreshed.RepairHistory.Count);
        Assert.Equal(refreshed.RepairHistory.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id), refreshed.RepairHistory);
        Assert.Equal(usage, JsonSerializer.Serialize(await c.UsageEvents.AsNoTracking().ToArrayAsync()));
        using var safe = JsonDocument.Parse(JsonSerializer.Serialize(refreshed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        AdminTransactionDetailHttpTests.AssertSafe(safe.RootElement);
    }

    [Theory]
    [InlineData("PaymentOrders")]
    [InlineData("SubscriptionPeriods")]
    public async Task DetailRepeatableRead_ConcurrentRepairDoesNotMixEvidence(string afterTable)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        var capture = new Capture
        {
            AfterTable = afterTable,
            AfterRead = async () => { await using var writer = f.Context();
                await writer.Database.ExecuteSqlRawAsync("SET lock_timeout = '2s'");
                Assert.Equal(EntitlementRepairOutcome.Repaired, (await EntitlementRepairPostgresFixture.Repair(writer)
                    .RepairAsync(f.Orders[0].Id, f.Actor, "concurrent restore")).Response!.Result); }
        };
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(f.Database.Connection, pg => pg.UseNetTopologySuite()).AddInterceptors(capture).Options);
        var before = (await new AdminTransactionRepository(c).GetDetailAsync(f.Orders[0].Id))!;
        Assert.True(capture.Injected);
        Assert.Equal("Missing", before.Entitlement!.GrantStatus);
        Assert.True(before.RepairEligibility!.Eligible);
        Assert.Empty(before.RepairHistory);
        Assert.Equal(8, capture.Reads.Count);
        Assert.All(capture.Reads, sql =>
        {
            foreach (var name in new[] { "CheckoutUrl", "QrCode", "PasswordHash", "FOR UPDATE", "FOR SHARE" })
                Assert.DoesNotContain(name, sql, StringComparison.OrdinalIgnoreCase);
        });
        Assert.All(capture.Columns, names => Assert.DoesNotContain("RawPayload", names, StringComparer.OrdinalIgnoreCase));
        Assert.All(capture.Levels, level => Assert.Equal(IsolationLevel.RepeatableRead, level));
        Assert.Empty(c.ChangeTracker.Entries());
        await using var fresh = f.Context();
        var after = (await new AdminTransactionRepository(fresh).GetDetailAsync(f.Orders[0].Id))!;
        Assert.Equal("Granted", after.Entitlement!.GrantStatus);
        Assert.Single(after.RepairHistory);
    }

    private sealed class Capture : DbCommandInterceptor
    {
        internal string? AfterTable; internal Func<Task>? AfterRead; internal bool Injected;
        internal List<string> Reads = []; internal List<string[]> Columns = []; internal List<IsolationLevel> Levels = [];
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)) return result;
            Reads.Add(command.CommandText);
            Columns.Add(Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray());
            Levels.Add(command.Transaction!.IsolationLevel);
            if (!Injected && command.CommandText.Contains($"\"{AfterTable}\"", StringComparison.Ordinal))
            { Injected = true; await AfterRead!(); }
            return result;
        }
    }
}
