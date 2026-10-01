using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LocalMateAI.Tests;

internal sealed class EntitlementRepairPostgresFixture(IsolatedPlanDatabase database, PaymentOrder[] orders,
    Guid actor, SubscriptionPeriod[] original) : IAsyncDisposable
{
    internal IsolatedPlanDatabase Database => database;
    internal PaymentOrder[] Orders => orders;
    internal Guid Actor => actor;
    internal SubscriptionPeriod[] Original => original;
    internal AppDbContext Context() => database.Context();
    internal static readonly DateTime Day = EntitlementRepairTests.Day;
    internal static PaymentSettlementService Settlement(AppDbContext c, DateTime at) =>
        new(new PaymentSettlementExecutor(c, new PaymentEvidenceTestClock(at)), new SubscriptionRepository(c),
            new UserRepository(c), new EmailOutboxRepository(c), new PaymentEvidenceTestClock(at),
            NullLogger<PaymentSettlementService>.Instance);
    internal static EntitlementRepairService Repair(AppDbContext c) =>
        new(new EntitlementRepairExecutor(c, new PaymentEvidenceTestClock(Day.AddDays(90))));

    internal static async Task<EntitlementRepairPostgresFixture> Create(int count = 1, int[]? missing = null, bool ties = false, bool custom = false)
    {
        var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: "20260930174809_AddPaymentEvidenceHistory");
        try
        {
            await using var c = db.Context();
            var roleId = await TestRoles.GetUserRoleIdAsync(c);
            var actor = Guid.Parse("00000000-0000-0000-0000-000000000003");
            var owner = new User { FullName = "Repair owner", Email = "repair-owner@fixture.local", RoleId = roleId };
            c.Users.AddRange(owner, new User { Id = actor, FullName = "Repair operator", Email = "repair-actor@fixture.local", RoleId = roleId });
            await c.SaveChangesAsync();
            var plan = await c.SubscriptionPlans.SingleAsync(p => p.Code == "TRIP_PASS");
            if (custom)
            {
                plan = new SubscriptionPlan { Code = "REPAIR_CUSTOM", Name = "Custom repair fixture",
                    EntitlementPriority = 7000, IsActive = false, IsSystem = false };
                c.SubscriptionPlans.Add(plan); await c.SaveChangesAsync();
                await new SubscriptionRepository(c).PublishVersionAsync(new SubscriptionPlanVersion
                {
                    PlanId = plan.Id, VersionNumber = 1, Price = 79000, DurationDays = 11,
                    GenerateLimit = 4, SavedTripLimit = 3, Origin = PlanVersionOrigin.Published, PublishedAt = Day
                });
            }
            var orders = Enumerable.Range(0, count).Select(i => new PaymentOrder
            {
                UserId = owner.Id, PlanId = plan.Id, PlanVersionId = plan.CurrentVersionId,
                PlanCode = custom ? null : PlanCode.TripPass, PlanVersionBinding = PlanVersionBinding.Native,
                Amount = custom ? 79000 : 19000, Status = PaymentOrderStatus.Pending,
                Type = i == 0 ? PaymentOrderType.Purchase : PaymentOrderType.Renewal,
                ProviderOrderCode = 800000 + i, ExpiresAt = Day.AddMinutes(15)
            }).ToArray();
            c.PaymentOrders.AddRange(orders);
            await c.SaveChangesAsync();
            foreach (var (order, i) in orders.Select((o, i) => (o, i)))
                Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c, ties ? Day : Day.AddDays(i))
                    .ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true))).Status);
            var periods = await c.SubscriptionPeriods.AsNoTracking().OrderBy(p => p.StartsAt).ToArrayAsync();
            var removed = (missing ?? [0]).Select(i => orders[i].Id).ToHashSet();
            if (removed.Count > 0)
                await SimulateLostPeriodsBeforeAuditMigration(c, periods.Where(p => !removed.Contains(p.SourcePaymentOrderId!.Value)));
            await c.GetService<IMigrator>().MigrateAsync();
            c.ChangeTracker.Clear();
            return new(db, await c.PaymentOrders.AsNoTracking().OrderBy(o => o.ProviderOrderCode).ToArrayAsync(), actor, periods);
        }
        catch { await db.DisposeAsync(); throw; }
    }

    private static async Task SimulateLostPeriodsBeforeAuditMigration(AppDbContext c, IEnumerable<SubscriptionPeriod> retained)
    {
        var name = new NpgsqlConnectionStringBuilder(c.Database.GetConnectionString()).Database;
        if (name is null || !name.StartsWith("localmate_s1b_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Recovery fixture is restricted to its disposable database.");
        Assert.Empty(await c.UsageEvents.ToListAsync());
        Assert.DoesNotContain("20261001041525_AddEntitlementRepairAudits", await c.Database.GetAppliedMigrationsAsync());
        // Simulate a lost-table restore, not an application write. No trigger/constraint is disabled.
        await c.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "UsageEvents", "SubscriptionPeriods" """);
        foreach (var p in retained)
            await c.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SubscriptionPeriods" ("Id","UserId","PlanId","PlanVersionId","StartsAt","EndsAt",
                    "SourcePaymentOrderId","LegacyUserSubscriptionId","CreatedAt","UpdatedAt")
                VALUES ({p.Id},{p.UserId},{p.PlanId},{p.PlanVersionId},{p.StartsAt},{p.EndsAt},
                    {p.SourcePaymentOrderId},{p.LegacyUserSubscriptionId},{p.CreatedAt},{p.UpdatedAt})
                """);
        c.ChangeTracker.Clear();
    }
    public ValueTask DisposeAsync() => database.DisposeAsync();
}
