using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Reservation = LocalMateAI.Tests.SubscriptionUpgradeReservationPostgresTests;
using Compatibility = LocalMateAI.Tests.SubscriptionUpgradeCompatibilityPostgresTests;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeHardVerificationPostgresTests
{
    private static AppDbContext ObservedContext(IsolatedPlanDatabase db, LockCapture capture) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Connection,
            o => o.UseNetTopologySuite()).AddInterceptors(capture).Options);

    [Theory]
    [InlineData("settlement")]
    [InlineData("release")]
    [InlineData("repair")]
    public async Task ExecutedSqlUsesCanonicalLockOrderAndOrderedMultiSourceLocks(string operation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var user = await Reservation.Seed(seed);
        var version = await seed.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await Reservation.PaidPeriod(seed, user, version, Reservation.Now);
        var order = await Reservation.Prepared(seed, user,
            operation == "release" ? PaymentOrderStatus.Failed : PaymentOrderStatus.Pending);
        Assert.Equal(2, await seed.PaymentOrderCredits.CountAsync(c => c.OrderId == order.Id));
        if (operation == "repair")
            Assert.Equal(PaymentSettlementStatus.Settled,
                (await Compatibility.Settlement(seed).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true))).Status);

        var capture = new LockCapture();
        await using var c = ObservedContext(db, capture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (operation == "settlement")
            Assert.Equal(PaymentSettlementStatus.Settled, (await Compatibility.Settlement(c)
                .ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true), timeout.Token)).Status);
        else if (operation == "release")
        {
            var gateway = new Reservation.Gateway { Amount = order.Amount, Before = () =>
            {
                Assert.Null(c.Database.CurrentTransaction);
                Assert.Empty(capture.Commands);
                return Task.CompletedTask;
            } };
            Assert.Equal(UpgradeReleaseStatus.Released,
                (await Reservation.Service(c, gateway).ResolveForReplacementAsync(user, order.Id, timeout.Token)).Status);
            Assert.Equal(1, gateway.Calls);
        }
        else
            Assert.Equal(EntitlementRepairOutcome.AlreadyGranted,
                (await new EntitlementRepairExecutor(c, Reservation.Clock)
                    .ExecuteAsync(order.Id, user, "Verify existing upgrade grant", timeout.Token))!.Result);

        AssertCanonicalLocks(capture);
    }

    [Fact]
    public async Task SettlementReleaseAndRepairQueueOnSameUserWithoutDeadlockOrDoubleCredit()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var user = await Reservation.Seed(seed);
        var order = await Reservation.Prepared(seed, user);
        var source = await seed.SubscriptionPeriods.AsNoTracking().SingleAsync();
        var rawEndsAt = source.EndsAt;
        var captures = new[] { new LockCapture(), new LockCapture(), new LockCapture() };
        await using var settlementContext = ObservedContext(db, captures[0]);
        await using var releaseContext = ObservedContext(db, captures[1]);
        await using var repairContext = ObservedContext(db, captures[2]);
        await using var blocker = db.Context();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={user} FOR UPDATE""").SingleAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var settlement = Compatibility.Settlement(settlementContext)
            .ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true), timeout.Token);
        var release = Reservation.Service(releaseContext, new() { Amount = order.Amount })
            .ResolveForReplacementAsync(user, order.Id, timeout.Token);
        var repair = new EntitlementRepairExecutor(repairContext, Reservation.Clock)
            .ExecuteAsync(order.Id, user, "Verify concurrent repair decision", timeout.Token);
        var all = Task.WhenAll((Task)settlement, release, repair);
        var blockerReleased = false;
        try
        {
            await Task.WhenAll(captures.Select(c => c.UserLockAttempt.Task))
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(settlement.IsCompleted);
            Assert.False(release.IsCompleted);
            Assert.False(repair.IsCompleted);
            await transaction.CommitAsync();
            blockerReleased = true;
            await all.WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            // Unblock and drain writers even if the synchronization assertion fails.
            if (!all.IsCompleted)
            {
                timeout.Cancel();
                if (!blockerReleased) await transaction.RollbackAsync();
                try { await all; } catch (OperationCanceledException) { }
            }
        }

        var stored = await seed.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(order.Amount, stored.Amount);
        Assert.Equal(order.CreditAmount, stored.CreditAmount);
        Assert.Equal(order.PlanVersionId, stored.PlanVersionId);
        Assert.Equal(order.ProviderOrderCode, stored.ProviderOrderCode);
        var claim = await seed.PaymentOrderCredits.AsNoTracking().SingleAsync(c => c.OrderId == order.Id);
        var period = await seed.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.Id == source.Id);
        Assert.Equal(rawEndsAt, period.EndsAt);
        var grants = await seed.SubscriptionPeriods.AsNoTracking().Where(p => p.SourcePaymentOrderId == order.Id).ToListAsync();
        var paidHistory = await seed.PaymentOrderStatusHistories.CountAsync(h => h.PaymentOrderId == order.Id && h.ToStatus == PaymentOrderStatus.Paid);
        var receipts = await seed.EmailOutboxMessages.CountAsync(e => e.DeduplicationKey == $"payment-receipt:{order.Id}");
        if (stored.Status == PaymentOrderStatus.Paid)
        {
            Assert.Equal(PaymentSettlementStatus.Settled, (await settlement).Status);
            var grant = Assert.Single(grants);
            Assert.Equal(Reservation.Now, grant.StartsAt);
            Assert.Equal(Reservation.Now.AddDays(30), grant.EndsAt);
            Assert.Null(claim.ReleasedAt);
            Assert.Equal(order.Id, period.TerminatedByOrderId);
            Assert.Equal(1, paidHistory);
            Assert.Equal(1, receipts);
        }
        else
        {
            Assert.Equal(PaymentOrderStatus.ReviewRequired, stored.Status);
            Assert.Equal(PaymentSettlementStatus.CreditConflict, (await settlement).Status);
            Assert.True(claim.HasValidReleaseEvidence());
            Assert.Null(stored.PaidAt);
            Assert.Null(period.TerminatedAt);
            Assert.Empty(grants);
            Assert.Equal(0, paidHistory);
            Assert.Equal(0, receipts);
        }
        Assert.NotNull(await repair);
        Assert.Single(await seed.EntitlementRepairAudits.Where(a => a.PaymentOrderId == order.Id).ToListAsync());
        Assert.False(await seed.EntitlementRepairAudits.AnyAsync(a => a.PaymentOrderId == order.Id && a.Outcome == EntitlementRepairOutcome.Repaired));
        Assert.Empty(await seed.PaymentWebhookReceipts.ToListAsync());
        Assert.All(captures, c => Assert.Equal("Users", LockTable(c.Commands.First())));
        Assert.Equal(0L, await seed.Database.SqlQueryRaw<long>(
            """SELECT deadlocks AS "Value" FROM pg_stat_database WHERE datname=current_database()""").SingleAsync());
    }

    [Fact]
    public async Task SystemSettingsToS1ToS3PreservesEvidenceAtEachMigrationCheckpoint()
    {
        const string previous = "20261002035306_AddSystemSettings";
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: previous);
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var plan = SubscriptionBaseline.PlanId(PlanCode.TripPass);
        var version = SubscriptionBaseline.VersionId(PlanCode.TripPass);
        var native = Guid.NewGuid();
        var single = Guid.NewGuid();
        var now = Reservation.Now;
        // Old-schema fixtures use only columns that exist at the SystemSettings checkpoint.
        await using (var tx = await c.Database.BeginTransactionAsync())
        {
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "PaymentOrders" ("Id","UserId","PlanId","PlanVersionId","PlanVersionBinding",
                "Amount","Type","Status","PaidAt","ExpiresAt","ProviderOrderCode","CreatedAt","UpdatedAt")
                VALUES ({native},{user.Id},{plan},{version},'Native',19000,'Purchase','Paid',{now},{now},19001,{now},{now});
                INSERT INTO "SubscriptionPeriods" ("Id","UserId","PlanId","PlanVersionId","SourcePaymentOrderId",
                "StartsAt","EndsAt","CreatedAt","UpdatedAt")
                VALUES ({Guid.NewGuid()},{user.Id},{plan},{version},{native},{now},{now.AddDays(7)},{now},{now});
                INSERT INTO "PaymentOrders" ("Id","UserId","ProductKind","SingleItineraryProductVersionId","CheckoutAttemptId",
                "PlanVersionBinding","Amount","Type","Status","PaidAt","ExpiresAt","ProviderOrderCode","CreatedAt","UpdatedAt")
                VALUES ({single},{user.Id},'SingleItinerary',{SingleItineraryBaseline.VersionId},{Guid.NewGuid()},NULL,
                29000,'Purchase','Paid',{now},{now},29001,{now},{now});
                INSERT INTO "SingleItineraryEntitlements" ("Id","UserId","SourcePaymentOrderId","SingleItineraryProductVersionId","GrantedAt")
                VALUES ({Guid.NewGuid()},{user.Id},{single},{SingleItineraryBaseline.VersionId},{now});
                INSERT INTO "PaymentOrders" ("Id","UserId","PlanCode","PlanVersionBinding","Amount","Type","Status",
                "ExpiresAt","ProviderOrderCode","CreatedAt","UpdatedAt")
                VALUES ({Guid.NewGuid()},{user.Id},'TripPass','LegacyUnresolved',49000,'Purchase','Expired',{now},49001,{now},{now});
                """);
            await tx.CommitAsync();
        }
        c.SystemSettings.Add(new SystemSetting
        {
            Key = LocalMateAI.Application.Settings.SystemSettingKeys.MinActivePlacesPerStation,
            Value = "8", UpdatedAt = now, UpdatedByUserId = user.Id
        });
        await c.SaveChangesAsync();
        var before = await EvidenceSnapshot(c);
        foreach (var checkpoint in new[] { Reservation.Previous, Reservation.Migration })
        {
            await c.GetService<IMigrator>().MigrateAsync(checkpoint);
            Assert.Equal(checkpoint, (await c.Database.GetAppliedMigrationsAsync()).Last());
            Assert.Equal(before, await EvidenceSnapshot(c));
            Assert.Equal(0, await c.PaymentOrderCredits.CountAsync());
            Assert.All(await c.PaymentOrders.AsNoTracking().ToListAsync(), o =>
            {
                Assert.Equal(0, o.CreditAmount);
                Assert.NotEqual(PaymentOrderType.Upgrade, o.Type);
            });
            Assert.All(await c.SubscriptionPeriods.AsNoTracking().ToListAsync(), p =>
            {
                Assert.Null(p.TerminatedAt);
                Assert.Null(p.TerminatedByOrderId);
            });
        }
        Assert.False(c.Database.HasPendingModelChanges());
        var indexes = await c.Database.SqlQueryRaw<string>(
            """SELECT indexname AS "Value" FROM pg_indexes WHERE schemaname='public'""").ToListAsync();
        Assert.Contains("UX_PaymentOrders_PendingSubscriptionUser", indexes);
        Assert.Contains("UX_OrderCredits_UnreleasedPeriod", indexes);
        Assert.Contains("IX_PaymentOrders_PaidAt_Paid", indexes);
        Assert.Contains("IX_SystemSettings_UpdatedByUserId", indexes);
    }

    [Fact]
    public async Task S3DowngradeRejectsDurableReleaseProofWithoutLosingEvidence()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await Reservation.Seed(c);
        var order = await Reservation.Prepared(c, user);
        Assert.Equal(UpgradeReleaseStatus.Released, (await Reservation.Service(c,
            new() { Amount = order.Amount }).ResolveForReplacementAsync(user, order.Id)).Status);
        const string snapshotSql = """SELECT to_jsonb(c)::text AS "Value" FROM "PaymentOrderCredits" c ORDER BY "PeriodId" """;
        var before = await c.Database.SqlQueryRaw<string>(snapshotSql).ToListAsync();
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            c.GetService<IMigrator>().MigrateAsync(Reservation.Previous));
        Assert.Contains("durable credit release proof exists", ex.MessageText);
        Assert.Equal(Reservation.Migration, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(before, await c.Database.SqlQueryRaw<string>(snapshotSql).ToListAsync());
        Assert.True((await c.PaymentOrderCredits.AsNoTracking().SingleAsync()).HasValidReleaseEvidence());
        Assert.Equal(PaymentOrderStatus.Failed, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
        Assert.False(c.Database.HasPendingModelChanges());
    }

    private static async Task<string[]> EvidenceSnapshot(AppDbContext c)
    {
        var rows = new List<string>();
        rows.AddRange(await c.Database.SqlQueryRaw<string>(
            """SELECT (to_jsonb(o)-'CreditAmount')::text AS "Value" FROM "PaymentOrders" o ORDER BY "Id" """).ToListAsync());
        rows.AddRange(await c.Database.SqlQueryRaw<string>(
            """SELECT (to_jsonb(p)-'TerminatedAt'-'TerminatedByOrderId')::text AS "Value" FROM "SubscriptionPeriods" p ORDER BY "Id" """).ToListAsync());
        rows.AddRange(await c.Database.SqlQueryRaw<string>(
            """SELECT to_jsonb(e)::text AS "Value" FROM "SingleItineraryEntitlements" e ORDER BY "Id" """).ToListAsync());
        rows.AddRange(await c.Database.SqlQueryRaw<string>(
            """SELECT to_jsonb(v)::text AS "Value" FROM "SingleItineraryProductVersions" v ORDER BY "Id" """).ToListAsync());
        rows.AddRange(await c.Database.SqlQueryRaw<string>(
            """SELECT to_jsonb(s)::text AS "Value" FROM "SystemSettings" s ORDER BY "Key" """).ToListAsync());
        return rows.ToArray();
    }

    private static string LockTable(string sql) => Regex.Match(sql, "FROM\\s+\"([^\"]+)\"",
        RegexOptions.IgnoreCase).Groups[1].Value;

    private static void AssertCanonicalLocks(LockCapture capture)
    {
        var commands = capture.Commands.ToArray();
        Assert.Equal(new[] { "Users", "PaymentOrders", "PaymentOrderCredits", "SubscriptionPeriods" },
            commands.Select(LockTable).ToArray());
        Assert.Contains("ORDER BY \"PeriodId\"", commands[2]);
        Assert.Contains("ORDER BY \"Id\"", commands[3]);
    }

    private sealed class LockCapture : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public TaskCompletionSource<bool> UserLockAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Enqueue(command.CommandText);
                if (LockTable(command.CommandText) == "Users") UserLockAttempt.TrySetResult(true);
            }
            return ValueTask.FromResult(result);
        }
    }
}
