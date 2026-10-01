using System.Text.Json;
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

public sealed class EntitlementRepairPostgresTests
{
    private static readonly DateTime Day = EntitlementRepairPostgresFixture.Day;
    private static Task<EntitlementRepairServiceResult> Repair(AppDbContext c, EntitlementRepairPostgresFixture f, int index = 0) =>
        EntitlementRepairPostgresFixture.Repair(c).RepairAsync(f.Orders[index].Id, f.Actor, "  Khôi phục lịch sử  ");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FirstMiddleLast_ExactHistoricalPurchasedVersion_RepeatDoesNotExtend(int target)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create(3, [target]);
        await using var c = f.Context();
        var beforeOrders = JsonSerializer.Serialize(await c.PaymentOrders.AsNoTracking().OrderBy(o => o.Id).ToArrayAsync());
        var before = await Counts(c);
        var result = (await Repair(c, f, target)).Response!;
        Assert.Equal(EntitlementRepairOutcome.Repaired, result.Result);
        var original = f.Original.Single(p => p.SourcePaymentOrderId == f.Orders[target].Id);
        Assert.Equal(original.StartsAt, result.StartsAt); Assert.Equal(original.EndsAt, result.EndsAt);
        Assert.True(result.EndsAt < Day.AddDays(90));
        Assert.Equal("DeterministicHistoricalReplay", result.ReconstructionMode);
        var repeat = (await Repair(c, f, target)).Response!;
        Assert.Equal(EntitlementRepairOutcome.AlreadyGranted, repeat.Result);
        Assert.Equal(result.SubscriptionPeriodId, repeat.SubscriptionPeriodId);
        Assert.Equal(result.StartsAt, repeat.StartsAt); Assert.Equal(result.EndsAt, repeat.EndsAt);
        Assert.Equal(before, await Counts(c));
        Assert.Equal(beforeOrders, JsonSerializer.Serialize(await c.PaymentOrders.AsNoTracking().OrderBy(o => o.Id).ToArrayAsync()));
        var stored = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == f.Orders[target].Id);
        Assert.Equal(f.Orders[target].PlanVersionId, stored.PlanVersionId);
        Assert.Equal(f.Orders[target].UserId, stored.UserId);
        var audits = await c.EntitlementRepairAudits.OrderBy(a => a.OccurredAt).ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.Single(audits, a => a.Outcome == EntitlementRepairOutcome.Repaired);
        Assert.All(audits, a => { Assert.Equal("Khôi phục lịch sử", a.Reason); Assert.Equal(f.Actor, a.ActorUserId); Assert.Equal(Day.AddDays(90), a.OccurredAt); });
    }

    [Fact]
    public async Task InactivePlanAndNewCurrentVersion_DoNotReplacePurchasedSnapshot()
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context();
        var plan = await c.SubscriptionPlans.SingleAsync(p => p.Id == f.Orders[0].PlanId);
        var repository = new SubscriptionRepository(c);
        var version = new SubscriptionPlanVersion { PlanId = plan.Id, VersionNumber = 2, Price = 29000, DurationDays = 12,
            GenerateLimit = 4, SavedTripLimit = 9, Origin = PlanVersionOrigin.Published, PublishedAt = Day.AddDays(1) };
        await repository.PublishVersionAsync(version, []);
        plan.IsActive = false;
        await c.SaveChangesAsync();
        var result = (await Repair(c, f)).Response!;
        Assert.Equal(EntitlementRepairOutcome.Repaired, result.Result);
        Assert.Equal(Day.AddDays(7), result.EndsAt);
        Assert.Equal(f.Orders[0].PlanVersionId, (await c.SubscriptionPeriods.SingleAsync()).PlanVersionId);
        Assert.Equal(version.Id, plan.CurrentVersionId);
    }

    [Fact]
    public async Task CustomPlanWithoutEnum_RepairsExactPurchasedContract()
    {
        await using var f = await EntitlementRepairPostgresFixture.Create(custom: true);
        await using var c = f.Context();
        Assert.Null(f.Orders[0].PlanCode);
        var result = (await Repair(c, f)).Response!;
        Assert.Equal(EntitlementRepairOutcome.Repaired, result.Result);
        Assert.Equal(Day.AddDays(11), result.EndsAt);
        Assert.Equal(f.Orders[0].PlanVersionId, (await c.SubscriptionPeriods.SingleAsync()).PlanVersionId);
        Assert.False((await c.SubscriptionPlans.SingleAsync(p => p.Id == f.Orders[0].PlanId)).IsActive);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(90)]
    public async Task ExistingFutureOrExpiredPeriod_AlwaysAuditedAlreadyGranted(int nowOffset)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create(2, missing: []);
        await using var c = f.Context();
        var original = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == f.Orders[1].Id);
        var result = await new EntitlementRepairExecutor(c, new PaymentEvidenceTestClock(Day.AddDays(nowOffset)))
            .ExecuteAsync(f.Orders[1].Id, f.Actor, "existing evidence");
        Assert.Equal(EntitlementRepairOutcome.AlreadyGranted, result!.Result);
        Assert.Equal(original.Id, result.SubscriptionPeriodId);
        Assert.Equal(original.StartsAt, result.StartsAt); Assert.Equal(original.EndsAt, result.EndsAt);
        Assert.Equal(2, await c.SubscriptionPeriods.CountAsync());
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Pending)]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Expired)]
    public async Task NonPaidNativeOrder_RejectsAndAudits_WithoutReconciliation(PaymentOrderStatus status)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context();
        var source = f.Orders[0];
        var order = new PaymentOrder { UserId = source.UserId, PlanId = source.PlanId, PlanVersionId = source.PlanVersionId,
            PlanVersionBinding = PlanVersionBinding.Native, PlanCode = PlanCode.TripPass, Amount = 19000,
            Status = status, ProviderOrderCode = 880001, ExpiresAt = Day };
        c.PaymentOrders.Add(order); await c.SaveChangesAsync();
        var before = await Counts(c);
        var result = (await EntitlementRepairPostgresFixture.Repair(c).RepairAsync(order.Id, f.Actor, "not yet paid")).Response!;
        Assert.Equal(EntitlementRepairOutcome.NotEligible, result.Result);
        Assert.Equal("order_not_paid", result.DecisionCode);
        Assert.Equal(status, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
        Assert.Single(await c.EntitlementRepairAudits.ToListAsync());
        Assert.Equal(before, await Counts(c));
    }

    [Theory]
    [InlineData("tie", "historical_window_unproven", EntitlementRepairOutcome.NotEligible)]
    [InlineData("conflict", "historical_replay_conflict", EntitlementRepairOutcome.Conflict)]
    public async Task AmbiguousOrContradictoryHistory_IsAuditedWithoutGrant(string mutation, string code, EntitlementRepairOutcome outcome)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create(2, [0], ties: mutation == "tie");
        await using var c = f.Context();
        if (mutation == "conflict")
            await c.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "PaymentOrders" SET "PaidAt"={Day.AddDays(40)} WHERE "Id"={f.Orders[1].Id}""");
        var result = (await Repair(c, f)).Response!;
        Assert.Equal(outcome, result.Result); Assert.Equal(code, result.DecisionCode);
        Assert.Null(result.SubscriptionPeriodId);
        Assert.Equal(1, await c.SubscriptionPeriods.CountAsync());
        Assert.Equal(outcome, (await c.EntitlementRepairAudits.SingleAsync()).Outcome);
    }

    [Theory]
    [InlineData(PlanVersionBinding.LegacyUnresolved)]
    [InlineData(PlanVersionBinding.LegacyVerified)]
    [InlineData(PlanVersionBinding.LegacyApprovedBaseline)]
    public async Task LegacyBindings_NotExecutable_NoAmountInference(PlanVersionBinding binding)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context();
        var order = new PaymentOrder { UserId = f.Orders[0].UserId, PlanId = f.Orders[0].PlanId,
            PlanVersionId = binding == PlanVersionBinding.LegacyUnresolved ? null : f.Orders[0].PlanVersionId,
            PlanVersionBinding = binding, PlanCode = PlanCode.TripPass, Amount = 19000,
            Status = PaymentOrderStatus.Paid, PaidAt = Day.AddDays(-30), ExpiresAt = Day, ProviderOrderCode = 900900 };
        c.PaymentOrders.Add(order); await c.SaveChangesAsync();
        var result = (await EntitlementRepairPostgresFixture.Repair(c).RepairAsync(order.Id, f.Actor, "legacy review")).Response!;
        Assert.Equal(EntitlementRepairOutcome.NotEligible, result.Result);
        Assert.Equal("historical_binding_not_supported", result.DecisionCode);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
        Assert.Single(await c.EntitlementRepairAudits.ToListAsync());
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("truncate")]
    public async Task PostgreSqlAudit_IsInsertOnly(string operation)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context(); await Repair(c, f);
        var sql = operation switch
        {
            "update" => """UPDATE "EntitlementRepairAudits" SET "Reason"='changed'""",
            "delete" => """DELETE FROM "EntitlementRepairAudits" """,
            _ => """TRUNCATE TABLE "EntitlementRepairAudits" """
        };
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(sql))).SqlState);
        Assert.Single(await c.EntitlementRepairAudits.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EfAudit_ModificationOrDeletionRejected(bool delete)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context(); await Repair(c, f);
        var audit = await c.EntitlementRepairAudits.SingleAsync();
        if (delete) c.Remove(audit); else c.Entry(audit).Property(a => a.Reason).CurrentValue = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("period")]
    public async Task InsertFailure_RollsBackBothPeriodAndAudit_NoOrderHistoryOutboxMutation(string table)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context();
        var before = await Counts(c);
        var failureSql = """
            CREATE FUNCTION test_repair_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            RAISE EXCEPTION 'controlled repair failure' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER test_repair_failure BEFORE INSERT ON "__test_table__"
            FOR EACH ROW EXECUTE FUNCTION test_repair_failure();
            """.Replace("__test_table__", table == "audit" ? "EntitlementRepairAudits" : "SubscriptionPeriods");
        await c.Database.ExecuteSqlRawAsync(failureSql);
        await Assert.ThrowsAsync<DbUpdateException>(() => Repair(c, f));
        await using var verify = f.Context();
        Assert.Empty(await verify.SubscriptionPeriods.ToListAsync());
        Assert.Empty(await verify.EntitlementRepairAudits.ToListAsync());
        Assert.Equal(before, await Counts(verify));
        Assert.Equal(PaymentOrderStatus.Paid, (await verify.PaymentOrders.SingleAsync()).Status);
        Assert.Equal(Day, (await verify.PaymentOrders.SingleAsync()).PaidAt);
    }

    [Theory]
    [InlineData("wrong_audit_order")]
    [InlineData("duplicate_repaired")]
    [InlineData("wrong_period_owner")]
    [InlineData("wrong_period_version")]
    [InlineData("overlap")]
    public async Task ExistingAndNewDatabaseGuards_RejectCorruptLinks(string mutation)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create(2, [0]);
        await using var c = f.Context(); var result = (await Repair(c, f)).Response!;
        if (mutation is "wrong_audit_order" or "duplicate_repaired")
        {
            c.EntitlementRepairAudits.Add(new EntitlementRepairAudit { PaymentOrderId = mutation == "wrong_audit_order" ? f.Orders[1].Id : f.Orders[0].Id,
                SubscriptionPeriodId = result.SubscriptionPeriodId, ActorUserId = f.Actor, Reason = "guard test",
                Outcome = mutation == "wrong_audit_order" ? EntitlementRepairOutcome.AlreadyGranted : EntitlementRepairOutcome.Repaired,
                DecisionCode = "already_granted", OccurredAt = Day.AddDays(91) });
        }
        else
        {
            var sourceOrder = f.Orders[0];
            if (mutation == "overlap")
            {
                sourceOrder = new PaymentOrder { UserId = sourceOrder.UserId, PlanId = sourceOrder.PlanId,
                    PlanVersionId = sourceOrder.PlanVersionId, PlanVersionBinding = PlanVersionBinding.Native,
                    Status = PaymentOrderStatus.Paid, PaidAt = Day.AddDays(2), Amount = 19000,
                    ProviderOrderCode = 999900, ExpiresAt = Day };
                c.PaymentOrders.Add(sourceOrder);
            }
            c.SubscriptionPeriods.Add(new SubscriptionPeriod { UserId = mutation == "wrong_period_owner" ? f.Actor : f.Orders[0].UserId,
                PlanId = f.Orders[0].PlanId!.Value, PlanVersionId = mutation == "wrong_period_version" ? Guid.NewGuid() : f.Orders[0].PlanVersionId!.Value,
                SourcePaymentOrderId = sourceOrder.Id, StartsAt = Day, EndsAt = Day.AddDays(7) });
        }
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
        if (mutation == "overlap") Assert.Equal("23P01", Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task NotFoundAndInvalidReason_DoNotPersistAudit()
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var c = f.Context();
        Assert.Null((await EntitlementRepairPostgresFixture.Repair(c).RepairAsync(Guid.NewGuid(), f.Actor, "unknown")).Response);
        Assert.True((await EntitlementRepairPostgresFixture.Repair(c).RepairAsync(f.Orders[0].Id, f.Actor, "bad\nreason")).InvalidRequest);
        Assert.Empty(await c.EntitlementRepairAudits.ToListAsync());
    }

    [Fact]
    public async Task FreshChain_AndUpgradeFromWp7_AreAdditiveWithoutFinancialBackfill()
    {
        await using var fresh = await IsolatedPlanDatabase.CreateAsync();
        await using (var c = fresh.Context())
        {
            Assert.Empty(await c.EntitlementRepairAudits.ToListAsync());
            Assert.Equal(3, await c.SubscriptionPlans.CountAsync());
            Assert.False(c.Database.HasPendingModelChanges());
            Assert.Equal("20261001041525_AddEntitlementRepairAudits", (await c.Database.GetAppliedMigrationsAsync()).Last());
        }
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: "20260930174809_AddPaymentEvidenceHistory");
        await using var upgrade = db.Context();
        var orders = await AdminTransactionPostgresTests.SeedAsync(upgrade);
        var before = JsonSerializer.Serialize(await upgrade.PaymentOrders.AsNoTracking().OrderBy(o => o.Id).ToArrayAsync());
        var periods = JsonSerializer.Serialize(await upgrade.SubscriptionPeriods.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync());
        await upgrade.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(before, JsonSerializer.Serialize(await upgrade.PaymentOrders.AsNoTracking().OrderBy(o => o.Id).ToArrayAsync()));
        Assert.Equal(periods, JsonSerializer.Serialize(await upgrade.SubscriptionPeriods.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync()));
        Assert.Empty(await upgrade.EntitlementRepairAudits.ToListAsync());
        Assert.False(upgrade.Database.HasPendingModelChanges());
        Assert.Equal(8, orders.Length);
    }

    internal static async Task<string> Counts(AppDbContext c) => JsonSerializer.Serialize(new
    {
        history = await c.PaymentOrderStatusHistories.CountAsync(), outbox = await c.EmailOutboxMessages.CountAsync(),
        receipts = await c.PaymentWebhookReceipts.CountAsync(), usage = await c.UsageEvents.CountAsync()
    });
}
