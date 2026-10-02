using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeCompatibilityPostgresTests
{
    private static readonly DateTime Now = SubscriptionUpgradeReservationPostgresTests.Now;
    private static readonly TimeProvider Clock = SubscriptionUpgradeReservationPostgresTests.Clock;
    internal static PaymentSettlementService Settlement(AppDbContext c) => new(new PaymentSettlementExecutor(c, Clock),
        new SubscriptionRepository(c), new UserRepository(c), new EmailOutboxRepository(c), Clock,
        NullLogger<PaymentSettlementService>.Instance, creditRepository: new PaymentCreditRepository(c, Clock));

    [Fact]
    public async Task PaidUpgradeAdminSnapshotCsvRevenueAndOriginalLowerGrantRemainCorrect()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user, PaymentOrderStatus.Pending);
        await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        c.ChangeTracker.Clear();
        var capture = new ReadCapture();
        await using var read = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, p => p.UseNetTopologySuite()).AddInterceptors(capture).Options);
        var repo = new AdminTransactionRepository(read, Clock);
        var filter = new AdminTransactionFilter(null, null, PaymentOrderType.Upgrade, null, null);
        var page = await repo.GetTransactionsAsync(filter, new PagedQuery());
        var row = Assert.Single(page.Items);
        Assert.Equal(59000, row.ListPrice); Assert.Equal(10000, row.CreditAmount); Assert.Equal(49000, row.Amount);
        var summary = await repo.GetSummaryAsync(filter); Assert.Equal(49000, summary.GrossRevenue);
        Assert.Equal(0, summary.ReviewRequiredCount);
        var csv = AdminTransactionTests.ParseCsv(AdminTransactionCsv.Write((await repo.GetExportRowsAsync(filter, 10000)).Rows));
        Assert.Equal("49000", csv[1][13]); Assert.Equal("10000", csv[1][14]);
        var dashboard = await new AdminDashboardRepository(read).GetPaidRevenueAsync(Now, Now.AddDays(1));
        Assert.Equal(49000, dashboard.Revenue);
        capture.Reads.Clear(); capture.Columns.Clear();
        var detail = (await repo.GetDetailAsync(o.Id))!;
        Assert.Equal("already_granted", detail.RepairEligibility!.Code);
        var source = Assert.Single(detail.CreditSources); Assert.Equal("Consumed", source.State);
        Assert.Equal("TRIP_PASS", source.PlanCode); Assert.Null(source.ReleaseEvidence);
        Assert.Equal(9, capture.Reads.Count);
        Assert.All(capture.Reads, sql => Assert.DoesNotContain("FOR UPDATE", sql));
        Assert.All(capture.Columns, columns => Assert.DoesNotContain("RawPayload", columns));
        Assert.All(capture.Reads, sql => { Assert.DoesNotContain("CheckoutUrl", sql); Assert.DoesNotContain("QrCode", sql); });
        Assert.All(capture.Levels, level => Assert.Equal(IsolationLevel.RepeatableRead, level));
        Assert.Empty(read.ChangeTracker.Entries());
        var lowerOrder = await c.PaymentOrders.SingleAsync(x => x.Id != o.Id);
        var lower = (await repo.GetDetailAsync(lowerOrder.Id))!;
        Assert.Equal("Granted", lower.Entitlement!.GrantStatus);
        Assert.Equal("AlreadyGranted", (await new EntitlementRepairExecutor(c, Clock)
            .ExecuteAsync(lowerOrder.Id, user, "check historical grant"))!.Result.ToString());
        Assert.Equal(2, await c.SubscriptionPeriods.CountAsync());
        var outbox = await c.EmailOutboxMessages.SingleAsync(x => x.DeduplicationKey == $"payment-receipt:{o.Id}");
        Assert.Equal(EmailTemplateNames.UpgradePaymentReceipt, outbox.TemplateName);
        var receipt = Assert.IsType<UpgradePaymentReceiptEmailModel>(EmailOutboxModelRegistry.Default.Deserialize(outbox.TemplateName, outbox.Model));
        Assert.Equal("49.000đ", receipt.Amount); Assert.Equal("10.000đ", receipt.CreditAmount);
        Assert.Equal("30 ngày", receipt.Duration);
    }

    [Fact]
    public async Task ReservedReleasedAndReviewEvidence_FilterSummaryAndNoReceipt()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user);
        var repo = new AdminTransactionRepository(c, Clock);
        Assert.Equal("Reserved", Assert.Single((await repo.GetDetailAsync(o.Id))!.CreditSources).State);
        await SubscriptionUpgradeReservationPostgresTests.Service(c, new() { Amount = o.Amount }).ResolveForReplacementAsync(user, o.Id);
        var released = Assert.Single((await repo.GetDetailAsync(o.Id))!.CreditSources);
        Assert.Equal("Released", released.State); Assert.Equal(CreditReleaseEvidence.SafeReason, released.ReleaseEvidence!.ReasonCode);
        Assert.Equal(0, released.ReleaseEvidence.AmountPaid); Assert.Equal(o.Amount, released.ReleaseEvidence.AmountRemaining);
        await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        var detail = (await repo.GetDetailAsync(o.Id))!;
        Assert.Equal("ReviewRequired", detail.Transaction.Status); Assert.Equal(59000, detail.Transaction.ListPrice);
        Assert.Equal("Released", Assert.Single(detail.CreditSources).State);
        Assert.Equal("payment_review_required", detail.RepairEligibility!.Code); Assert.False(detail.RepairEligibility.Eligible);
        Assert.Contains(detail.StatusHistory, h => h.ReasonCode == "upgrade_credit_conflict");
        var filter = new AdminTransactionFilter(null, PaymentOrderStatus.ReviewRequired, PaymentOrderType.Upgrade, null, null);
        Assert.Single((await repo.GetTransactionsAsync(filter, new() { SortBy = "status" })).Items);
        var summary = await repo.GetSummaryAsync(filter);
        Assert.Equal(1, summary.TotalTransactions); Assert.Equal(1, summary.ReviewRequiredCount);
        Assert.Equal(0, summary.PaidCount + summary.PendingCount + summary.FailedCount + summary.ExpiredCount);
        Assert.Equal(0, summary.GrossRevenue);
        Assert.False(await c.EmailOutboxMessages.AnyAsync(x => x.DeduplicationKey == $"payment-receipt:{o.Id}"));
    }

    [Fact]
    public async Task LaterUpgradeTerminationPreservesBothEarlierGrants()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var first = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user, PaymentOrderStatus.Pending);
        await Settlement(c).ApplyVerifiedPaymentAsync(new(first.ProviderOrderCode, first.Amount, true));
        var (top, _) = await PlanVersionFoundationPostgresTests.CustomAsync(c, "COMPAT_TOP", 300, 100000, 20, null, null);
        var next = await SubscriptionUpgradeSettlementPostgresTests.Checkout(c, new()).CheckoutAsync(user, top.Code);
        var second = await c.PaymentOrders.SingleAsync(o => o.Id == next.Response!.OrderId);
        await Settlement(c).ApplyVerifiedPaymentAsync(new(second.ProviderOrderCode, second.Amount, true));
        foreach (var id in await c.PaymentOrders.Where(o => o.Id != second.Id).Select(o => o.Id).ToListAsync())
        {
            Assert.Equal("already_granted", (await new AdminTransactionRepository(c, Clock).GetDetailAsync(id))!.RepairEligibility!.Code);
            Assert.Equal(EntitlementRepairOutcome.AlreadyGranted, (await new EntitlementRepairExecutor(c, Clock)
                .ExecuteAsync(id, user, "preserve historical termination"))!.Result);
        }
        Assert.Equal(3, await c.SubscriptionPeriods.CountAsync());
        Assert.Empty(await c.EntitlementRepairAudits.Where(a => a.Outcome == EntitlementRepairOutcome.Repaired).ToListAsync());
    }

    [Fact]
    public async Task ConflictSourceHasNoMutationAndNoSuccessfulReceipt()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user);
        var legacy = new UserSubscription { UserId = user, PlanCode = PlanCode.Membership, StartsAt = Now, EndsAt = Now.AddDays(30) };
        c.Add(legacy); await c.SaveChangesAsync();
        c.Add(new SubscriptionPeriod { UserId = user, PlanId = o.PlanId!.Value, PlanVersionId = o.PlanVersionId!.Value,
            StartsAt = legacy.StartsAt, EndsAt = legacy.EndsAt, LegacyUserSubscriptionId = legacy.Id });
        await c.SaveChangesAsync();
        await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        var detail = (await new AdminTransactionRepository(c, Clock).GetDetailAsync(o.Id))!;
        Assert.Equal("Conflict", Assert.Single(detail.CreditSources).State);
        Assert.Equal("payment_review_required", detail.RepairEligibility!.Code);
        Assert.False(await c.EmailOutboxMessages.AnyAsync(x => x.DeduplicationKey == $"payment-receipt:{o.Id}"));
    }

    [Fact]
    public async Task ReceiptInsertFailureRollsBackEntireUpgradeSettlement()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user, PaymentOrderStatus.Pending);
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Connection,
            p => p.UseNetTopologySuite()).AddInterceptors(new RejectReceipt()).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true)));
        Assert.Equal(PaymentOrderStatus.Pending, (await seed.PaymentOrders.AsNoTracking().SingleAsync(x => x.Id == o.Id)).Status);
        Assert.All(await seed.SubscriptionPeriods.AsNoTracking().ToListAsync(), p => Assert.Null(p.TerminatedAt));
        Assert.False(await seed.SubscriptionPeriods.AnyAsync(p => p.SourcePaymentOrderId == o.Id));
        Assert.False(await seed.EmailOutboxMessages.AnyAsync(e => e.DeduplicationKey == $"payment-receipt:{o.Id}"));
        Assert.False(await seed.PaymentOrderStatusHistories.AnyAsync(h => h.PaymentOrderId == o.Id && h.ToStatus == PaymentOrderStatus.Paid));
    }

    private sealed class RejectReceipt : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"EmailOutboxMessages\"", StringComparison.Ordinal))
                throw new InvalidOperationException("controlled Upgrade outbox failure");
            return ValueTask.FromResult(result);
        }
    }

    // Simulate an old lost-table restore BEFORE audit/Upgrade guards existed, then install the
    // full unchanged migration chain. No trigger or constraint is disabled for any repair test.
    internal static async Task<(IsolatedPlanDatabase Db, PaymentOrder Order, Guid User)> MissingTarget()
    {
        var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: "20260930174809_AddPaymentEvidenceHistory");
        try
        {
            await using var old = db.ContextBeforeSingleItinerary();
            var user = await PlanVersionFoundationPostgresTests.UserAsync(old);
            var actorId = Guid.Parse("00000000-0000-0000-0000-000000000003");
            if (!await old.Users.AnyAsync(u => u.Id == actorId))
            {
                old.Users.Add(new() { Id = actorId, FullName = "Repair operator", Email = "operator@upgrade.test", RoleId = user.RoleId });
                await old.SaveChangesAsync();
            }
            var trip = await old.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
            var sourceOrder = new PaymentOrder { UserId = user.Id, PlanId = trip.PlanId, PlanVersionId = trip.Id,
                PlanCode = PlanCode.TripPass, PlanVersionBinding = PlanVersionBinding.Native, Amount = 19000,
                Type = PaymentOrderType.Purchase, ProviderOrderCode = 510001, ExpiresAt = Now };
            old.Add(sourceOrder); await old.SaveChangesAsync();
            await EntitlementRepairPostgresFixture.Settlement(old, Now.AddDays(-2))
                .ApplyVerifiedPaymentAsync(new(sourceOrder.ProviderOrderCode, sourceOrder.Amount, true));
            var source = await old.SubscriptionPeriods.AsNoTracking().SingleAsync();
            var target = await old.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.Membership));
            var order = new PaymentOrder { UserId = user.Id, PlanId = target.PlanId, PlanVersionId = target.Id,
                PlanCode = PlanCode.Membership, PlanVersionBinding = PlanVersionBinding.Native, Amount = 59000,
                Type = PaymentOrderType.Upgrade, Status = PaymentOrderStatus.Paid, PaidAt = Now,
                ProviderOrderCode = 510002, ExpiresAt = Now };
            var period = new SubscriptionPeriod { UserId = user.Id, PlanId = target.PlanId, PlanVersionId = target.Id,
                SourcePaymentOrderId = order.Id, StartsAt = Now, EndsAt = Now.AddDays(30) };
            old.AddRange(order, period); await old.SaveChangesAsync();
            await EntitlementRepairPostgresFixture.SimulateLostPeriodsBeforeAuditMigration(old, [source]);
            await using var c = db.Context();
            await c.GetService<IMigrator>().MigrateAsync(SubscriptionUpgradeReservationPostgresTests.Previous);
            var savedSource = await c.SubscriptionPeriods.SingleAsync();
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "PaymentOrderCredits" ("OrderId","PeriodId","UserId","OriginalEndsAt","RemainingDays","CalculatedCreditAmount","ReleasedAt")
                VALUES ({order.Id},{savedSource.Id},{user.Id},{savedSource.EndsAt},{0},{0m},NULL)
                """);
            savedSource.Terminate(Now, order.Id); await c.SaveChangesAsync();
            await c.GetService<IMigrator>().MigrateAsync();
            return (db, await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id), user.Id);
        }
        catch { await db.DisposeAsync(); throw; }
    }

    [Fact]
    public async Task MissingTargetPreviewAndHttpRepairExactInterval_TargetOnly_NoFinancialSideEffects()
    {
        var f = await MissingTarget(); await using var db = f.Db; await using var c = db.Context();
        var repo = new AdminTransactionRepository(c, Clock);
        var preview = (await repo.GetDetailAsync(f.Order.Id))!;
        Assert.True(preview.RepairEligibility!.Eligible);
        var before = await Snapshot(c);
        using var host = new AdminTransactionsHttpTests.Host(repair: new EntitlementRepairService(new EntitlementRepairExecutor(c, Clock)));
        host.Authenticate("manage");
        var response = await host.Client.PostAsJsonAsync($"/api/admin/transactions/{f.Order.Id}/repair-entitlement", new { reason = " Restore original target " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UpgradeDeterministicHistoricalReplay", json.RootElement.GetProperty("reconstructionMode").GetString());
        var target = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == f.Order.Id);
        Assert.Equal(Now, target.StartsAt); Assert.Equal(Now.AddDays(30), target.EndsAt); Assert.Equal(f.Order.PlanVersionId, target.PlanVersionId);
        Assert.Equal(before, await Snapshot(c));
        Assert.Equal(EntitlementRepairOutcome.AlreadyGranted, (await new EntitlementRepairExecutor(c, Clock).ExecuteAsync(f.Order.Id, f.User, "retry"))!.Result);
        Assert.Single(await c.EntitlementRepairAudits.Where(a => a.Outcome == EntitlementRepairOutcome.Repaired).ToListAsync());
        Assert.Equal("already_granted", (await repo.GetDetailAsync(f.Order.Id))!.RepairEligibility!.Code);
    }

    [Fact]
    public async Task S5C1_TwoRepairsOneHistoricalGrantAndOneRepairedAudit()
    {
        var f = await MissingTarget(); await using var db = f.Db;
        await using var a = db.Context(); await using var b = db.Context();
        var results = await Task.WhenAll(new EntitlementRepairExecutor(a, Clock).ExecuteAsync(f.Order.Id, f.User, "first"),
            new EntitlementRepairExecutor(b, Clock).ExecuteAsync(f.Order.Id, f.User, "second"));
        Assert.Contains(results, r => r!.Result == EntitlementRepairOutcome.Repaired);
        Assert.Contains(results, r => r!.Result == EntitlementRepairOutcome.AlreadyGranted);
        await using var c = db.Context();
        Assert.Single(await c.SubscriptionPeriods.Where(p => p.SourcePaymentOrderId == f.Order.Id).ToListAsync());
        Assert.Single(await c.EntitlementRepairAudits.Where(a => a.Outcome == EntitlementRepairOutcome.Repaired).ToListAsync());
    }

    [Fact]
    public async Task S5C2_ConcurrentTargetInsertWinsUserLock_RepairAlreadyGranted()
    {
        var f = await MissingTarget(); await using var db = f.Db; await using var writer = db.Context();
        await using var tx = await writer.Database.BeginTransactionAsync();
        await writer.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Users\" WHERE \"Id\"={f.User} FOR UPDATE").SingleAsync();
        writer.SubscriptionPeriods.Add(new() { UserId = f.User, PlanId = f.Order.PlanId!.Value, PlanVersionId = f.Order.PlanVersionId!.Value,
            SourcePaymentOrderId = f.Order.Id, StartsAt = Now, EndsAt = Now.AddDays(30) });
        await writer.SaveChangesAsync();
        await using var repair = db.Context();
        var task = new EntitlementRepairExecutor(repair, Clock).ExecuteAsync(f.Order.Id, f.User, "concurrent restore");
        await tx.CommitAsync();
        Assert.Equal(EntitlementRepairOutcome.AlreadyGranted, (await task)!.Result);
        Assert.Single(await writer.SubscriptionPeriods.Where(p => p.SourcePaymentOrderId == f.Order.Id).ToListAsync());
    }

    [Fact]
    public async Task S5C3_ConcurrentEvidenceMutationRejected_RepairUsesImmutableEvidence()
    {
        var f = await MissingTarget(); await using var db = f.Db; await using var a = db.Context(); await using var b = db.Context();
        var mutation = Assert.ThrowsAsync<Npgsql.PostgresException>(() => b.Database.ExecuteSqlAsync(
            $"UPDATE \"PaymentOrderCredits\" SET \"OriginalEndsAt\"=\"OriginalEndsAt\" + interval '1 day' WHERE \"OrderId\"={f.Order.Id}"));
        var repair = new EntitlementRepairExecutor(a, Clock).ExecuteAsync(f.Order.Id, f.User, "immutable source proof");
        await mutation; Assert.Equal(EntitlementRepairOutcome.Repaired, (await repair)!.Result);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("period")]
    public async Task ControlledFailureRollsBackTargetAndAudit(string failure)
    {
        var f = await MissingTarget(); await using var db = f.Db;
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, p => p.UseNetTopologySuite()).AddInterceptors(new FailInsert(failure)).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EntitlementRepairExecutor(c, Clock).ExecuteAsync(f.Order.Id, f.User, "rollback"));
        await using var fresh = db.Context();
        Assert.False(await fresh.SubscriptionPeriods.AnyAsync(p => p.SourcePaymentOrderId == f.Order.Id));
        Assert.Empty(await fresh.EntitlementRepairAudits.ToListAsync());
        Assert.Equal(PaymentOrderStatus.Paid, (await fresh.PaymentOrders.SingleAsync(o => o.Id == f.Order.Id)).Status);
    }

    private static async Task<string> Snapshot(AppDbContext c) => JsonSerializer.Serialize(new
    {
        orders = await c.PaymentOrders.AsNoTracking().OrderBy(o => o.Id).ToArrayAsync(),
        sources = await c.SubscriptionPeriods.AsNoTracking().Where(p => p.TerminatedAt != null).OrderBy(p => p.Id).ToArrayAsync(),
        claims = await c.PaymentOrderCredits.AsNoTracking().OrderBy(p => p.PeriodId).ToArrayAsync(),
        history = await c.PaymentOrderStatusHistories.AsNoTracking().OrderBy(h => h.Id).ToArrayAsync(),
        outbox = await c.EmailOutboxMessages.AsNoTracking().OrderBy(e => e.Id).ToArrayAsync(),
        usage = await c.UsageEvents.AsNoTracking().ToArrayAsync(), receipts = await c.PaymentWebhookReceipts.AsNoTracking().ToArrayAsync()
    });
    private sealed class FailInsert(string phase) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (phase == "audit" && data.Context!.ChangeTracker.Entries<EntitlementRepairAudit>().Any(e => e.State == EntityState.Added)
                || phase == "period" && data.Context!.ChangeTracker.Entries<SubscriptionPeriod>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("controlled repair rollback");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class ReadCapture : DbCommandInterceptor
    {
        internal List<string> Reads = []; internal List<string[]> Columns = []; internal List<IsolationLevel> Levels = [];
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (command.Transaction is not null)
            { Reads.Add(command.CommandText); Columns.Add(Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray());
                Levels.Add(command.Transaction.IsolationLevel); }
            return ValueTask.FromResult(result);
        }
    }
}
