using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
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
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeReservationPostgresTests
{
    internal static readonly DateTime Now = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
    internal const string Previous = "20261002044046_AddSubscriptionUpgradeFoundation";
    internal const string Migration = "20261002060553_AddSubscriptionUpgradeFinancialSafety";
    internal static readonly TimeProvider Clock = new PlanVersionFoundationPostgresTests.Clock(Now);
    internal static SubscriptionUpgradeReservationService Service(AppDbContext c, Gateway? gateway = null,
        IPaymentCreditRepository? credits = null) => new(new SubscriptionRepository(c), new PaymentOrderRepository(c, Clock),
        credits ?? new PaymentCreditRepository(c, Clock), new PaymentOperationExecutor(c), gateway ?? new(), Clock);

    internal static async Task<Guid> Seed(AppDbContext c)
    {
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var version = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(c, u.Id, version, Now.AddDays(-2));
        return u.Id;
    }

    internal static async Task<SubscriptionPeriod> PaidPeriod(AppDbContext c, Guid user, SubscriptionPlanVersion v, DateTime paidAt)
    {
        var o = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user, v.PlanId, v);
        Assert.Equal(PaymentSettlementStatus.Settled, (await PlanVersionFoundationPostgresTests.Settlement(c,
            new PlanVersionFoundationPostgresTests.Clock(paidAt)).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true))).Status);
        return await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == o.Id);
    }

    internal static async Task<PaymentOrder> Prepared(AppDbContext c, Guid user, PaymentOrderStatus status = PaymentOrderStatus.Failed)
    {
        var result = await Service(c).PrepareAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        var order = await c.PaymentOrders.SingleAsync(o => o.Id == result.OrderId);
        if (status != PaymentOrderStatus.Pending)
            await new PaymentOrderRepository(c, Clock).TransitionStatusAsync(order, status,
                new(PaymentStatusChangeSource.LocalExpiration, user, ReasonCode: "controlled_test_terminal"), Now);
        return order;
    }

    internal sealed class Gateway(PaymentGatewayOrderStatus status = PaymentGatewayOrderStatus.Cancelled) : IPaymentGateway
    {
        public decimal Amount { get; set; } = 49000;
        public string? Flaw { get; set; }
        public int Calls;
        public Func<Task>? Before { get; set; }
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Upgrade preparation must not create a link");
        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string raw, CancellationToken ct = default) =>
            throw new InvalidOperationException("Release must not verify a webhook");
        public async Task<PaymentGatewayOrderResult> GetPaymentAsync(long code, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            if (Before is not null) await Before();
            if (Flaw == "timeout") throw new TimeoutException();
            if (Flaw == "unavailable") return PaymentGatewayOrderResult.Unavailable(code);
            return new(true, Flaw == "code" ? code + 1 : code, Amount, status)
            {
                RequestedAmount = Amount, AmountPaid = Flaw == "missing_paid" ? null : status == PaymentGatewayOrderStatus.Underpaid ? 100 : 0,
                AmountRemaining = status == PaymentGatewayOrderStatus.Underpaid ? Amount - 100 : Amount
            };
        }
    }

    [Fact]
    public async Task PreparationSnapshotsAllSourcesAndRecomputesTargetVersion()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await Seed(c);
        var trip = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(c, user, trip, Now);
        var legacy = new UserSubscription { UserId = user, PlanCode = PlanCode.TripPass, StartsAt = Now.AddDays(20), EndsAt = Now.AddDays(40) };
        c.Add(legacy); await c.SaveChangesAsync();
        c.Add(new SubscriptionPeriod { UserId = user, PlanId = trip.PlanId, PlanVersionId = trip.Id,
            LegacyUserSubscriptionId = legacy.Id, StartsAt = legacy.StartsAt, EndsAt = legacy.EndsAt });
        await c.SaveChangesAsync();
        await PlanVersionFoundationPostgresTests.PublishAsync(c, SubscriptionBaseline.PlanId(PlanCode.Membership), 2, 69000, 45, null, null);
        var result = await Service(c).PrepareAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        var o = await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == result.OrderId);
        Assert.Equal(69000, o.Amount + o.CreditAmount); Assert.Null(o.CheckoutUrl); Assert.Null(o.QrCode);
        Assert.Equal(2, (await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == o.PlanVersionId)).VersionNumber);
        var rows = await c.PaymentOrderCredits.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count); Assert.Contains(rows, r => r.CalculatedCreditAmount == 0 && r.RemainingDays == 21);
        Assert.Equal(19000, rows.Max(r => r.CalculatedCreditAmount));
        Assert.Single(await c.PaymentOrderStatusHistories.Where(h => h.PaymentOrderId == o.Id).ToListAsync());
        Assert.Equal(3, await c.SubscriptionPeriods.CountAsync()); Assert.Equal(2, await c.EmailOutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("future")]
    [InlineData("pending")]
    [InlineData("inactive")]
    [InlineData("not_upgrade")]
    public async Task RechecksCurrentStateAndNeverReservesFromStalePreview(string changed)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await Seed(c);
        var member = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.Membership));
        if (changed == "future") await PaidPeriod(c, user, member, Now.AddDays(1));
        if (changed == "pending")
        {
            var pending = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user, member.PlanId, member);
            pending.ExpiresAt = Now.AddMinutes(10); await c.SaveChangesAsync();
        }
        if (changed == "inactive") { (await c.SubscriptionPlans.SingleAsync(p => p.Id == member.PlanId)).IsActive = false; await c.SaveChangesAsync(); }
        var result = await Service(c).PrepareAsync(user, changed == "not_upgrade" ? "TripPass" : "Membership");
        Assert.NotEqual(PaymentIntentResultStatus.Success, result.Status);
        Assert.Empty(await c.PaymentOrderCredits.ToListAsync());
        Assert.DoesNotContain(await c.PaymentOrders.ToListAsync(), o => o.Type == PaymentOrderType.Upgrade);
    }

    [Theory]
    [InlineData("claims")]
    [InlineData("release")]
    public async Task ControlledRollbackKeepsWholeTransactionAtomic(string phase)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await Seed(seed);
        var version = await seed.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(seed, user, version, Now);
        PaymentOrder? order = phase == "release" ? await Prepared(seed, user) : null;
        var beforeOrders = await seed.PaymentOrders.CountAsync();
        var beforeHistory = await seed.PaymentOrderStatusHistories.CountAsync();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Connection,
            o => o.UseNetTopologySuite()).AddInterceptors(new FailAfterCredits()).Options);
        if (phase == "claims") await Assert.ThrowsAsync<InvalidOperationException>(() => Service(c).PrepareAsync(user, "Membership"));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Service(c, new() { Amount = order!.Amount }).ResolveForReplacementAsync(user, order.Id));
        Assert.Equal(beforeOrders, await seed.PaymentOrders.CountAsync()); Assert.Equal(beforeHistory, await seed.PaymentOrderStatusHistories.CountAsync());
        Assert.All(await seed.PaymentOrderCredits.AsNoTracking().ToListAsync(), r => Assert.Null(r.ReleasedAt));
        if (phase == "claims") Assert.Empty(await seed.PaymentOrderCredits.ToListAsync());
        else Assert.Equal(2, await seed.PaymentOrderCredits.CountAsync());
    }

    private sealed class FailAfterCredits : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<PaymentOrderCredit>().Any()) throw new InvalidOperationException("controlled rollback");
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, "none", UpgradeReleaseStatus.Released)]
    [InlineData(PaymentGatewayOrderStatus.Paid, "none", UpgradeReleaseStatus.RequiresSettlement)]
    [InlineData(PaymentGatewayOrderStatus.Underpaid, "none", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Pending, "none", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Processing, "none", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Unknown, "none", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Expired, "none", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Failed, "none", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, "missing_paid", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, "timeout", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, "code", UpgradeReleaseStatus.Blocked)]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, "unavailable", UpgradeReleaseStatus.Blocked)]
    public async Task ProviderProofMatrix_NoMutationUnlessComplete(PaymentGatewayOrderStatus providerStatus, string flaw, UpgradeReleaseStatus expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await Seed(c); var order = await Prepared(c, user);
        var histories = await c.PaymentOrderStatusHistories.CountAsync(); var outbox = await c.EmailOutboxMessages.CountAsync();
        var gateway = new Gateway(providerStatus) { Amount = order.Amount, Flaw = flaw };
        var result = await Service(c, gateway).ResolveForReplacementAsync(user, order.Id);
        Assert.Equal(expected, result.Status); Assert.Equal(1, gateway.Calls);
        var claim = await c.PaymentOrderCredits.AsNoTracking().SingleAsync();
        Assert.Equal(expected == UpgradeReleaseStatus.Released, claim.HasValidReleaseEvidence());
        Assert.Equal(histories, await c.PaymentOrderStatusHistories.CountAsync()); Assert.Equal(outbox, await c.EmailOutboxMessages.CountAsync());
        Assert.Equal(PaymentOrderStatus.Failed, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
    }

    [Fact]
    public async Task PendingNotProbed_ExpiredPendingCanProbe_ProviderNetworkHasNoLocks()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await Seed(c); var order = await Prepared(c, user, PaymentOrderStatus.Pending);
        var gateway = new Gateway { Amount = order.Amount, Before = async () =>
        {
            Assert.Null(c.Database.CurrentTransaction);
            await using var probe = db.Context(); await using var tx = await probe.Database.BeginTransactionAsync();
            await probe.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={user} FOR UPDATE NOWAIT""").SingleAsync();
            await probe.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "PaymentOrders" WHERE "Id"={order.Id} FOR UPDATE NOWAIT""").SingleAsync();
        } };
        Assert.Equal(UpgradeReleaseStatus.Blocked, (await Service(c, gateway).ResolveForReplacementAsync(user, order.Id)).Status);
        Assert.Equal(0, gateway.Calls);
        order.ExpiresAt = Now; await c.SaveChangesAsync();
        Assert.Equal(UpgradeReleaseStatus.Released, (await Service(c, gateway).ResolveForReplacementAsync(user, order.Id)).Status);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, await c.PaymentOrderStatusHistories.CountAsync(h => h.PaymentOrderId == order.Id && h.ToStatus == PaymentOrderStatus.Expired));
        Assert.Equal(UpgradeReleaseStatus.AlreadyReleased, (await Service(c, gateway).ResolveForReplacementAsync(user, order.Id)).Status);
        Assert.Equal(1, gateway.Calls);
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("proofless")]
    [InlineData("delete")]
    [InlineData("truncate")]
    [InlineData("reopen")]
    [InlineData("proof_rewrite")]
    public async Task DatabaseReleaseEvidenceIsImmutable(string mutation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await Seed(c); var order = await Prepared(c, user);
        if (mutation is "reopen" or "proof_rewrite")
            Assert.Equal(UpgradeReleaseStatus.Released, (await Service(c, new() { Amount = order.Amount }).ResolveForReplacementAsync(user, order.Id)).Status);
        FormattableString sql = mutation switch
        {
            "metadata" => $"""UPDATE "PaymentOrderCredits" SET "RemainingDays"=99 WHERE "OrderId"={order.Id}""",
            "proofless" => $"""UPDATE "PaymentOrderCredits" SET "ReleasedAt"={Now} WHERE "OrderId"={order.Id}""",
            "delete" => $"""DELETE FROM "PaymentOrderCredits" WHERE "OrderId"={order.Id}""",
            "truncate" => $"""TRUNCATE "PaymentOrderCredits" """,
            "reopen" => $"""UPDATE "PaymentOrderCredits" SET "ReleasedAt"=NULL WHERE "OrderId"={order.Id}""",
            _ => $"""UPDATE "PaymentOrderCredits" SET "ReleaseProviderCheckedAt"={Now.AddSeconds(1)} WHERE "OrderId"={order.Id}"""
        };
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync(sql));
    }

    [Fact]
    public async Task MultiSourceReleaseThenReplacementRecomputesInsteadOfUsingOldSnapshot()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await Seed(c);
        var version = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(c, user, version, Now);
        var old = await Prepared(c, user);
        Assert.Equal(2, await c.PaymentOrderCredits.CountAsync());
        Assert.Equal(UpgradeReleaseStatus.Released, (await Service(c, new() { Amount = old.Amount }).ResolveForReplacementAsync(user, old.Id)).Status);
        Assert.All(await c.PaymentOrderCredits.AsNoTracking().ToListAsync(), r => Assert.True(r.HasValidReleaseEvidence()));
        var target = await PlanVersionFoundationPostgresTests.PublishAsync(c, old.PlanId!.Value, 2, 99000, 30, null, null);
        var next = await Service(c).PrepareAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, next.Status);
        var replacement = await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == next.OrderId);
        Assert.Equal(target.Id, replacement.PlanVersionId); Assert.Equal(99000, replacement.Amount + replacement.CreditAmount);
        Assert.NotEqual(old.Amount, replacement.Amount);
        Assert.Equal(4, await c.PaymentOrderCredits.CountAsync()); Assert.Equal(2, await c.PaymentOrderCredits.CountAsync(r => r.ReleasedAt == null));
        var periods = await c.SubscriptionPeriods.CountAsync(); var outbox = await c.EmailOutboxMessages.CountAsync();
        Assert.Equal(PaymentSettlementStatus.InvalidPaidPlan, (await PlanVersionFoundationPostgresTests.Settlement(c, Clock)
            .ApplyVerifiedPaymentAsync(new(old.ProviderOrderCode, old.Amount, true))).Status);
        Assert.Equal(periods, await c.SubscriptionPeriods.CountAsync()); Assert.Equal(outbox, await c.EmailOutboxMessages.CountAsync());
        Assert.All(await c.PaymentOrderCredits.AsNoTracking().Where(r => r.OrderId == old.Id).ToListAsync(), r => Assert.True(r.HasValidReleaseEvidence()));
    }

    [Theory]
    [InlineData(PaymentOrderType.Purchase)]
    [InlineData(PaymentOrderType.Renewal)]
    [InlineData(PaymentOrderType.Upgrade)]
    public async Task GlobalPendingIndexRejectsEverySubscriptionOperationButNotSingle(PaymentOrderType type)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var v = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, v.PlanId, v);
        await SingleItineraryPostgresTests.Order(c, user.Id); await SingleItineraryPostgresTests.Order(c, user.Id);
        var o = new PaymentOrder { UserId = user.Id, PlanId = v.PlanId, PlanVersionId = v.Id, PlanVersionBinding = PlanVersionBinding.Native,
            Type = type, Amount = v.Price, ExpiresAt = Now.AddMinutes(15) };
        await using var attempt = db.Context(); attempt.Add(o);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync());
        Assert.Equal("UX_PaymentOrders_PendingSubscriptionUser", ((PostgresException)ex.InnerException!).ConstraintName);
        Assert.Equal(3, await c.PaymentOrders.CountAsync());
    }

    [Fact]
    public async Task MigrationRejectsProoflessHistoricalReleaseWithoutChangingFinancialData()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: Previous); await using var c = db.Context();
        var user = await Seed(c);
        var p = await c.SubscriptionPeriods.SingleAsync();
        var v = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.Membership));
        var order = new PaymentOrder { UserId = user, PlanId = v.PlanId, PlanVersionId = v.Id, PlanVersionBinding = PlanVersionBinding.Native,
            Type = PaymentOrderType.Upgrade, Amount = v.Price, ExpiresAt = Now };
        c.Add(order); await c.SaveChangesAsync();
        // Raw SQL writes only old-schema columns; no modern release proof is invented.
        await c.Database.ExecuteSqlAsync($"""
            INSERT INTO "PaymentOrderCredits" ("OrderId","PeriodId","UserId","OriginalEndsAt","RemainingDays","CalculatedCreditAmount")
            VALUES ({order.Id},{p.Id},{user},{p.EndsAt},{4},{0m})
            """);
        await c.Database.ExecuteSqlAsync($"""UPDATE "PaymentOrderCredits" SET "ReleasedAt"={Now} WHERE "OrderId"={order.Id}""");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => c.Database.MigrateAsync());
        Assert.Contains("historical released claims lack provider proof", ex.MessageText);
        Assert.Equal(Previous, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(v.Price, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Amount);
    }

    [Theory]
    [InlineData("duplicate", false)]
    [InlineData("single", true)]
    [InlineData("none", true)]
    public async Task MigrationPreflightAndHistoricalMoneyPreservation(string setup, bool success)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: Previous); await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var v = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, v.PlanId, v);
        if (setup == "duplicate") await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, v.PlanId, v);
        if (setup == "single") { await SingleItineraryPostgresTests.Order(c, user.Id); await SingleItineraryPostgresTests.Order(c, user.Id); }
        var before = await c.Database.SqlQueryRaw<string>("""SELECT to_jsonb(o)::text AS "Value" FROM "PaymentOrders" o ORDER BY "Id" """).ToListAsync();
        if (success)
        {
            await c.Database.MigrateAsync(); Assert.Equal(Migration, (await c.Database.GetAppliedMigrationsAsync()).Last());
            Assert.False(c.Database.HasPendingModelChanges());
        }
        else
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => c.Database.MigrateAsync());
            Assert.Contains("duplicate Pending", ex.MessageText);
            Assert.Equal(Previous, (await c.Database.GetAppliedMigrationsAsync()).Last());
        }
        Assert.Equal(before, await c.Database.SqlQueryRaw<string>("""SELECT to_jsonb(o)::text AS "Value" FROM "PaymentOrders" o ORDER BY "Id" """).ToListAsync());
    }
}
