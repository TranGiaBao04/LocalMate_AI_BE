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
using Microsoft.Extensions.Options;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class PaymentEvidencePostgresTests
{
    private const string ParentMigration = "20260930143244_AddVersionedPlanFeatures";
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EmptyEvidenceMigration_CanRollBackToParentWithoutChangingExistingModel()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        await c.GetService<IMigrator>().MigrateAsync(ParentMigration);
        Assert.Equal(ParentMigration, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(3, await c.SubscriptionPlans.CountAsync());
        Assert.Equal(3, await c.SubscriptionPlanVersionFeatures.CountAsync());
        Assert.True(await c.Database.SqlQuery<bool>($"""
            SELECT to_regclass('"PaymentWebhookReceipts"') IS NULL AS "Value"
            """).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedEvidence_PreventsDestructiveMigrationRollback(bool history)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        if (history) await Order(c);
        else await new PaymentEvidenceRepository(c).SaveVerifiedReceiptAsync(PaymentEvidenceTests.Receipt(Now));
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.GetService<IMigrator>().MigrateAsync(ParentMigration));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(history ? 1 : 0, await c.PaymentOrderStatusHistories.CountAsync());
        Assert.Equal(history ? 0 : 1, await c.PaymentWebhookReceipts.CountAsync());
        Assert.EndsWith("AddPaymentEvidenceHistory", (await c.Database.GetAppliedMigrationsAsync()).Last());
    }

    [Fact]
    public async Task FreshChain_CreatesEmptyEvidence_RbacPlansAndIndexesRemain()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        Assert.Empty(await c.PaymentWebhookReceipts.ToListAsync());
        Assert.Empty(await c.PaymentOrderStatusHistories.ToListAsync());
        Assert.Equal(3, await c.SubscriptionPlans.CountAsync());
        Assert.Equal(3, await c.SubscriptionPlanVersions.CountAsync());
        Assert.Equal(3, await c.SubscriptionPlanVersionFeatures.CountAsync());
        Assert.True(await c.Roles.AnyAsync());
        Assert.Equal(0, await c.RolePermissions.CountAsync());
        Assert.False(c.Database.HasPendingModelChanges());
        var retentionIndex = await c.Database.SqlQuery<string>($"""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename='PaymentWebhookReceipts'
            AND indexname='IX_PaymentWebhookReceipts_RawPayloadRetainUntil'
            """).SingleAsync();
        Assert.Contains("WHERE", retentionIndex);
        Assert.Contains("IS NOT NULL", retentionIndex);
        var constraints = await c.Database.SqlQuery<string>($"""
            SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
            WHERE conrelid IN ('"PaymentWebhookReceipts"'::regclass,'"PaymentOrderStatusHistories"'::regclass)
              AND contype='f'
            """).ToListAsync();
        Assert.Equal(4, constraints.Count);
        Assert.All(constraints, s => Assert.Contains("ON DELETE RESTRICT", s));
    }

    [Fact]
    public async Task UpgradeFromParent_PreservesAllFinancialEntitlementUsageAndRbac_NoFakeHistory()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: ParentMigration);
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PlanVersionFoundationPostgresTests.TripAsync(c, user.Id);
        foreach (var (amount, plan, status) in new[]
        {
            (19000, "TripPass", "Paid"), (49000, "TripPass", "Pending"), (59000, "Membership", "Paid"),
            (19000, "TripPass", "Failed"), (49000, "TripPass", "Expired")
        })
            await c.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PaymentOrders" ("Id","UserId","PlanCode","Type","Amount","Status","ExpiresAt","PaidAt","CreatedAt","UpdatedAt","PlanVersionBinding")
                VALUES ({Guid.NewGuid()},{user.Id},{plan},'Purchase',{amount},{status},{Now.AddMinutes(15)},
                        {(status == "Paid" ? (DateTime?)Now : null)},{Now},{Now},'LegacyUnresolved')
                """);
        var legacy = new UserSubscription { UserId = user.Id, PlanCode = PlanCode.TripPass, StartsAt = Now.AddDays(-2), EndsAt = Now.AddDays(5) };
        c.UserSubscriptions.Add(legacy);
        c.SubscriptionPeriods.Add(new() { UserId = user.Id, PlanId = SubscriptionBaseline.PlanId(PlanCode.TripPass),
            PlanVersionId = SubscriptionBaseline.VersionId(PlanCode.TripPass), StartsAt = Now.AddDays(-2), EndsAt = Now.AddDays(5), LegacyUserSubscriptionId = legacy.Id });
        c.UsageEvents.Add(new() { UserId = user.Id, TripId = trip.Id, Type = UsageEventType.Generate });
        await c.SaveChangesAsync();
        string[] tables = ["PaymentOrders", "SubscriptionPeriods", "UserSubscriptions", "UsageEvents", "Users", "Roles", "RolePermissions",
            "SubscriptionPlans", "SubscriptionPlanVersions", "PlanFeatures", "SubscriptionPlanVersionFeatures"];
        var before = new Dictionary<string, string>();
        async Task<string> Snapshot(string table)
        {
            if (!tables.Contains(table)) throw new ArgumentException("Not a snapshot table.");
            var sql = $"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),'[]'::jsonb)::text AS \"Value\" FROM \"{table}\" t";
            return await c.Database.SqlQueryRaw<string>(sql).SingleAsync();
        }
        foreach (var table in tables) before[table] = await Snapshot(table);
        await c.Database.MigrateAsync();
        foreach (var table in tables) Assert.Equal(before[table], await Snapshot(table));
        Assert.Empty(await c.PaymentOrderStatusHistories.ToListAsync());
        Assert.Empty(await c.PaymentWebhookReceipts.ToListAsync());
        Assert.False(c.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("ok", "", 1)]
    [InlineData("unavailable", "gateway_unavailable", 2)]
    [InlineData("unusable", "unusable_payment_link", 2)]
    public async Task Checkout_CreatesInitialHistory_AndCapturesLinkFailures(string link, string reason, int count)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var gateway = new PaymentEvidenceTestGateway { Link = link switch
        { "ok" => PaymentLinkResult.Succeeded("https://example.invalid/test", "synthetic-qr"),
          "unusable" => PaymentLinkResult.Succeeded("", ""), _ => PaymentLinkResult.Unavailable() } };
        await Payment(c, gateway).CheckoutAsync(user.Id, "TripPass");
        var history = await c.PaymentOrderStatusHistories.OrderBy(h => h.OccurredAt).ToListAsync();
        Assert.Equal(count, history.Count);
        var initial = Assert.Single(history, h => h.FromStatus is null);
        Assert.Equal(PaymentOrderStatus.Pending, initial.ToStatus);
        Assert.Equal("order_created", initial.ReasonCode);
        Assert.Equal(PaymentStatusChangeSource.Checkout, initial.Source);
        Assert.Equal(user.Id, initial.ActorUserId);
        if (count == 2)
        {
            var failure = Assert.Single(history, h => h.FromStatus is not null);
            Assert.Equal(PaymentOrderStatus.Failed, failure.ToStatus);
            Assert.Equal(reason, failure.ReasonCode);
            Assert.Equal(PaymentOrderStatus.Failed, (await c.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        }
    }

    [Theory]
    [InlineData(false, "unusable_pending_payment_link", PaymentStatusChangeSource.Checkout)]
    [InlineData(true, "local_expired", PaymentStatusChangeSource.LocalExpiration)]
    public async Task Checkout_ExistingUnusableOrExpiredPending_CapturedAtomically(bool expired, string reason, PaymentStatusChangeSource source)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c);
        order.ExpiresAt = expired ? Now.AddMinutes(-1) : Now.AddMinutes(15);
        await c.SaveChangesAsync();
        await Payment(c, new() { Link = PaymentLinkResult.Succeeded("https://example.invalid/test", "synthetic") }).CheckoutAsync(order.UserId, "TripPass");
        var transition = Assert.Single(await c.PaymentOrderStatusHistories.Where(h => h.PaymentOrderId == order.Id && h.FromStatus != null).ToListAsync());
        Assert.Equal(source, transition.Source);
        Assert.Equal(reason, transition.ReasonCode);
        Assert.Equal(2, await c.PaymentOrders.CountAsync());
    }

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, PaymentOrderStatus.Failed, "provider_cancelled")]
    [InlineData(PaymentGatewayOrderStatus.Underpaid, PaymentOrderStatus.Failed, "provider_underpaid")]
    [InlineData(PaymentGatewayOrderStatus.Failed, PaymentOrderStatus.Failed, "provider_failed")]
    [InlineData(PaymentGatewayOrderStatus.Expired, PaymentOrderStatus.Expired, "provider_expired")]
    [InlineData(PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Paid, "provider_paid")]
    public async Task OwnedLookup_CapturesProviderTransitionAndActor_WithoutRepeat(PaymentGatewayOrderStatus provider, PaymentOrderStatus expected, string reason)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c, usableLink: true);
        var gateway = new PaymentEvidenceTestGateway { Lookup = new(true, order.ProviderOrderCode, order.Amount, provider) };
        var service = Payment(c, gateway);
        await service.GetOrderAsync(order.UserId, order.Id);
        await service.GetOrderAsync(order.UserId, order.Id);
        var transition = Assert.Single(await c.PaymentOrderStatusHistories.Where(h => h.PaymentOrderId == order.Id && h.FromStatus != null).ToListAsync());
        Assert.Equal(PaymentStatusChangeSource.ProviderLookup, transition.Source);
        Assert.Equal(order.UserId, transition.ActorUserId);
        Assert.Equal(reason, transition.ReasonCode);
        Assert.Null(transition.WebhookReceiptId);
        Assert.Equal(expected, transition.ToStatus);
        Assert.Equal(expected, (await c.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Pending)]
    [InlineData(PaymentGatewayOrderStatus.Processing)]
    [InlineData(PaymentGatewayOrderStatus.Unknown)]
    public async Task OwnedLookup_NoChange_NoAdditionalHistory(PaymentGatewayOrderStatus provider)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c, usableLink: true);
        await Payment(c, new() { Lookup = new(true, order.ProviderOrderCode, order.Amount, provider) }).GetOrderAsync(order.UserId, order.Id);
        Assert.Single(await c.PaymentOrderStatusHistories.ToListAsync());
        Assert.Equal(PaymentOrderStatus.Pending, (await c.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task LocalExpiry_UsesHistoryAndUserOrderLocks_ThenLatePaymentRemainsPossible()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c, usableLink: true);
        order.ExpiresAt = Now.AddMinutes(-1);
        await c.SaveChangesAsync();
        await Payment(c, new() { Lookup = new(true, order.ProviderOrderCode, order.Amount, PaymentGatewayOrderStatus.Pending) }).GetOrderAsync(order.UserId, order.Id);
        var expiry = Assert.Single(await c.PaymentOrderStatusHistories.Where(h => h.FromStatus != null).ToListAsync());
        Assert.Equal(PaymentStatusChangeSource.ProviderLookup, expiry.Source);
        Assert.Equal(PaymentOrderStatus.Expired, expiry.ToStatus);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true))).Status);
        Assert.Single(await c.SubscriptionPeriods.ToListAsync());
        Assert.Equal(3, await c.PaymentOrderStatusHistories.CountAsync());
    }

    [Theory]
    [InlineData(false, 19000, "verified_non_success")]
    [InlineData(true, 49000, "amount_mismatch")]
    public async Task VerifiedFailureOrMismatch_CapturesFailed_ThenLatePaid(bool success, decimal amount, string reason)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c);
        var gateway = new PaymentEvidenceTestGateway { Verification = PaymentWebhookVerificationResult.Valid(new(order.ProviderOrderCode, amount, success)) };
        await Webhook(c, gateway).ProcessAsync("verified-failure");
        var failure = Assert.Single(await c.PaymentOrderStatusHistories.Where(h => h.FromStatus != null).ToListAsync());
        Assert.Equal(reason, failure.ReasonCode);
        Assert.Equal(PaymentOrderStatus.Failed, failure.ToStatus);
        Assert.NotNull(failure.WebhookReceiptId);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
        await Webhook(c, gateway).ProcessAsync("verified-failure-repeat");
        Assert.Equal(2, await c.PaymentOrderStatusHistories.CountAsync());
        gateway.Verification = PaymentWebhookVerificationResult.Valid(new(order.ProviderOrderCode, order.Amount, true));
        await Webhook(c, gateway).ProcessAsync("late-paid");
        Assert.Single(await c.SubscriptionPeriods.ToListAsync());
        Assert.Equal(3, await c.PaymentWebhookReceipts.CountAsync());
        Assert.Equal(3, await c.PaymentOrderStatusHistories.CountAsync());
        Assert.Equal(PaymentOrderStatus.Failed, (await c.PaymentOrderStatusHistories.SingleAsync(h => h.ToStatus == PaymentOrderStatus.Paid)).FromStatus);
    }

    [Fact]
    public async Task InvalidSignature_ZeroReceiptHistoryGrantAndMutation()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        await Order(c);
        var before = await c.PaymentOrders.AsNoTracking().SingleAsync();
        await Webhook(c, new()).ProcessAsync("invalid");
        var after = await c.PaymentOrders.AsNoTracking().SingleAsync();
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Empty(await c.PaymentWebhookReceipts.ToListAsync());
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
        Assert.Single(await c.PaymentOrderStatusHistories.ToListAsync());
    }

    [Fact]
    public async Task VerifiedUnknownOrder_IsAcknowledged_WithUnlinkedReceiptOnly()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var gateway = new PaymentEvidenceTestGateway { Verification = PaymentWebhookVerificationResult.Valid(new(7654321, 19000, true)) };
        Assert.Equal(PaymentWebhookStatus.Acknowledged, (await Webhook(c, gateway).ProcessAsync("verified-unknown")).Status);
        Assert.Null((await c.PaymentWebhookReceipts.SingleAsync()).PaymentOrderId);
        Assert.Empty(await c.PaymentOrderStatusHistories.ToListAsync());
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentVerifiedDuplicates_ThreeReceipts_OnePaidTransitionGrantAndOutbox()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var order = await Order(seed);
        async Task<PaymentWebhookResult> Callback()
        {
            await using var c = db.Context();
            return await Webhook(c, new() { Verification = PaymentWebhookVerificationResult.Valid(new(order.ProviderOrderCode, order.Amount, true)) }).ProcessAsync("same-verified-body");
        }
        Assert.All(await Task.WhenAll(Callback(), Callback(), Callback()), r => Assert.Equal(PaymentWebhookStatus.Acknowledged, r.Status));
        await using var verify = db.Context();
        Assert.Equal(3, await verify.PaymentWebhookReceipts.CountAsync());
        Assert.All(await verify.PaymentWebhookReceipts.ToListAsync(), r => Assert.Equal(order.Id, r.PaymentOrderId));
        Assert.Single(await verify.SubscriptionPeriods.ToListAsync());
        Assert.Single(await verify.EmailOutboxMessages.ToListAsync());
        Assert.Single(await verify.PaymentOrderStatusHistories.Where(h => h.ToStatus == PaymentOrderStatus.Paid).ToListAsync());
        Assert.Equal(2, await verify.PaymentOrderStatusHistories.CountAsync());
        Assert.Equal(Now, (await verify.PaymentOrders.SingleAsync()).PaidAt);
    }

    [Fact]
    public async Task HistoryInsertFailure_RollsBackStatusPaidAtGrantAndOutbox_ButReceiptSurvives()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c);
        await c.Database.ExecuteSqlRawAsync("ALTER TABLE \"PaymentOrderStatusHistories\" ADD CONSTRAINT test_no_paid CHECK (\"ToStatus\" <> 'Paid')");
        await Assert.ThrowsAsync<DbUpdateException>(() => Webhook(c, new() { Verification = PaymentWebhookVerificationResult.Valid(new(order.ProviderOrderCode, order.Amount, true)) }).ProcessAsync("verified-before-rollback"));
        await using var verify = db.Context();
        var stored = await verify.PaymentOrders.SingleAsync();
        Assert.Equal(PaymentOrderStatus.Pending, stored.Status);
        Assert.Null(stored.PaidAt);
        Assert.Empty(await verify.SubscriptionPeriods.ToListAsync());
        Assert.Empty(await verify.EmailOutboxMessages.ToListAsync());
        Assert.Single(await verify.PaymentOrderStatusHistories.ToListAsync());
        Assert.Single(await verify.PaymentWebhookReceipts.ToListAsync());
        await verify.Database.ExecuteSqlRawAsync("ALTER TABLE \"PaymentOrderStatusHistories\" DROP CONSTRAINT test_no_paid");
        await Webhook(verify, new() { Verification = PaymentWebhookVerificationResult.Valid(new(order.ProviderOrderCode, order.Amount, true)) }).ProcessAsync("retry-after-rollback");
        Assert.Equal(2, await verify.PaymentWebhookReceipts.CountAsync());
        Assert.Single(await verify.SubscriptionPeriods.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentExpiryAndPaid_NeverOverwritePaid_AndHistoryAgrees()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c);
        await using var expiring = db.Context();
        await using var paying = db.Context();
        var expire = new PaymentOrderRepository(expiring, new PaymentEvidenceTestClock(Now)).MarkExpiredIfPendingAsync(order.Id, Now);
        var pay = Settlement(paying).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true));
        await Task.WhenAll(expire, pay);
        Assert.Equal(PaymentSettlementStatus.Settled, (await pay).Status);
        await using var verify = db.Context();
        Assert.Equal(PaymentOrderStatus.Paid, (await verify.PaymentOrders.SingleAsync()).Status);
        Assert.Single(await verify.SubscriptionPeriods.ToListAsync());
        Assert.Single(await verify.PaymentOrderStatusHistories.Where(h => h.ToStatus == PaymentOrderStatus.Paid).ToListAsync());
        Assert.False(await new PaymentOrderRepository(verify).MarkExpiredIfPendingAsync(order.Id, Now));
        var count = await verify.PaymentOrderStatusHistories.CountAsync();
        Assert.True(count is 2 or 3);
    }

    [Theory]
    [InlineData("history_update")]
    [InlineData("history_delete")]
    [InlineData("history_truncate")]
    [InlineData("receipt_delete")]
    [InlineData("receipt_truncate")]
    [InlineData("receipt_id")]
    [InlineData("receipt_order")]
    [InlineData("receipt_provider")]
    [InlineData("receipt_amount")]
    [InlineData("receipt_success")]
    [InlineData("receipt_received")]
    [InlineData("receipt_hash")]
    [InlineData("receipt_retain_until")]
    [InlineData("receipt_raw_replace")]
    [InlineData("receipt_early_purge")]
    public async Task PostgreSqlGuards_RejectMutationDeleteAndTruncate(string action)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c);
        var historyId = (await c.PaymentOrderStatusHistories.SingleAsync()).Id;
        var receipt = PaymentEvidenceTests.Receipt(Now);
        await new PaymentEvidenceRepository(c).SaveVerifiedReceiptAsync(receipt);
        var sql = action switch
        {
            "history_update" => $"UPDATE \"PaymentOrderStatusHistories\" SET \"ReasonCode\"='changed' WHERE \"Id\"='{historyId}'",
            "history_delete" => $"DELETE FROM \"PaymentOrderStatusHistories\" WHERE \"Id\"='{historyId}'",
            "history_truncate" => "TRUNCATE \"PaymentOrderStatusHistories\"",
            "receipt_delete" => $"DELETE FROM \"PaymentWebhookReceipts\" WHERE \"Id\"='{receipt.Id}'",
            "receipt_truncate" => "TRUNCATE \"PaymentWebhookReceipts\" CASCADE",
            _ => $"UPDATE \"PaymentWebhookReceipts\" SET {action switch
            {
                "receipt_id" => "\"Id\"=gen_random_uuid()",
                "receipt_order" => $"\"PaymentOrderId\"='{order.Id}'",
                "receipt_provider" => "\"ProviderOrderCode\"=1",
                "receipt_amount" => "\"Amount\"=1",
                "receipt_success" => "\"IsSuccessful\"=false",
                "receipt_received" => "\"ReceivedAt\"=\"ReceivedAt\"-interval '1 day'",
                "receipt_hash" => "\"RawPayloadSha256\"=repeat('a',64)",
                "receipt_retain_until" => "\"RawPayloadRetainUntil\"=\"RawPayloadRetainUntil\"-interval '1 day'",
                "receipt_raw_replace" => "\"RawPayload\"='replacement'",
                "receipt_early_purge" => "\"RawPayload\"=NULL,\"RawPayloadPurgedAt\"=\"RawPayloadRetainUntil\"",
                _ => throw new ArgumentException("Unknown guard test.")
            }} WHERE \"Id\"='{receipt.Id}'"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal("23514", error.SqlState);
        if (action == "receipt_truncate") Assert.Equal("Webhook receipts cannot be deleted or truncated", error.MessageText);
        c.ChangeTracker.Clear();
        Assert.Single(await c.PaymentOrderStatusHistories.ToListAsync());
        Assert.Equal("verified-body", (await c.PaymentWebhookReceipts.SingleAsync()).RawPayload);
    }

    [Fact]
    public async Task Retention_PurgesOnlyExpiredRaw_MetadataPermanent_SecondRunNoOp()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var repo = new PaymentEvidenceRepository(c);
        var due = PaymentEvidenceTests.Receipt(DateTime.UtcNow.Date.AddDays(-31));
        var recent = PaymentEvidenceTests.Receipt(DateTime.UtcNow.Date.AddDays(-29));
        await repo.SaveVerifiedReceiptAsync(due);
        await repo.SaveVerifiedReceiptAsync(recent);
        var now = DateTime.UtcNow;
        Assert.Equal(1, await repo.PurgeExpiredRawPayloadsAsync(now));
        Assert.Equal(0, await repo.PurgeExpiredRawPayloadsAsync(now.AddMinutes(1)));
        c.ChangeTracker.Clear();
        var stored = await c.PaymentWebhookReceipts.SingleAsync(r => r.Id == due.Id);
        Assert.Null(stored.RawPayload);
        Assert.NotNull(stored.RawPayloadPurgedAt);
        Assert.Equal(due.RawPayloadSha256, stored.RawPayloadSha256);
        Assert.Equal(due.RawPayloadRetainUntil, stored.RawPayloadRetainUntil);
        Assert.Equal(due.ReceivedAt, stored.ReceivedAt);
        Assert.Equal(due.PaymentOrderId, stored.PaymentOrderId);
        Assert.Equal(due.ProviderOrderCode, stored.ProviderOrderCode);
        Assert.Equal(due.Amount, stored.Amount);
        Assert.Equal(due.IsSuccessful, stored.IsSuccessful);
        Assert.NotNull((await c.PaymentWebhookReceipts.SingleAsync(r => r.Id == recent.Id)).RawPayload);
        var sql = $"UPDATE \"PaymentWebhookReceipts\" SET \"RawPayload\"='resurrected',\"RawPayloadPurgedAt\"=NULL WHERE \"Id\"='{due.Id}'";
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(sql))).SqlState);
    }

    [Fact]
    public async Task EfAllowsExpiredPurge_AndPostgresAllowsOnlyRawAndPurgeTimestamp()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var receipt = PaymentEvidenceTests.Receipt(DateTime.UtcNow.AddDays(-31));
        await new PaymentEvidenceRepository(c).SaveVerifiedReceiptAsync(receipt);
        receipt.RawPayload = null;
        receipt.RawPayloadPurgedAt = DateTime.UtcNow;
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        Assert.Null((await c.PaymentWebhookReceipts.SingleAsync()).RawPayload);
    }

    [Theory]
    [InlineData("history_order")]
    [InlineData("history_actor")]
    [InlineData("history_receipt")]
    [InlineData("receipt_order")]
    public async Task EvidenceForeignKeys_RejectUnknownReferences(string reference)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c);
        if (reference == "receipt_order")
        {
            var receipt = PaymentEvidenceTests.Receipt(Now);
            receipt.PaymentOrderId = Guid.NewGuid();
            c.PaymentWebhookReceipts.Add(receipt);
        }
        else c.PaymentOrderStatusHistories.Add(new()
        {
            PaymentOrderId = reference == "history_order" ? Guid.NewGuid() : order.Id,
            FromStatus = PaymentOrderStatus.Pending, ToStatus = PaymentOrderStatus.Failed,
            Source = PaymentStatusChangeSource.Webhook, OccurredAt = Now,
            ActorUserId = reference == "history_actor" ? Guid.NewGuid() : null,
            WebhookReceiptId = reference == "history_receipt" ? Guid.NewGuid() : null
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
        Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task SameStatusHistory_IsRejectedByDatabaseConstraint()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Order(c);
        c.PaymentOrderStatusHistories.Add(new() { PaymentOrderId = order.Id, FromStatus = order.Status,
            ToStatus = order.Status, Source = PaymentStatusChangeSource.ProviderLookup, OccurredAt = Now });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
        Assert.Equal("23514", Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    private static async Task<PaymentOrder> Order(AppDbContext c, bool usableLink = false)
    {
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var version = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        var order = new PaymentOrder { UserId = user.Id, PlanCode = PlanCode.TripPass, Type = PaymentOrderType.Purchase,
            PlanId = version.PlanId, PlanVersionId = version.Id, PlanVersionBinding = PlanVersionBinding.Native,
            Status = PaymentOrderStatus.Pending, Amount = version.Price, ExpiresAt = Now.AddMinutes(15),
            CheckoutUrl = usableLink ? "https://example.invalid/test" : null, QrCode = usableLink ? "synthetic-only" : null };
        await new PaymentOrderRepository(c, new PaymentEvidenceTestClock(Now)).AddAsync(order);
        return order;
    }
    private static PaymentSettlementService Settlement(AppDbContext c) => new(new PaymentSettlementExecutor(c, new PaymentEvidenceTestClock(Now)),
        new SubscriptionRepository(c), new UserRepository(c), new EmailOutboxRepository(c), new PaymentEvidenceTestClock(Now), NullLogger<PaymentSettlementService>.Instance);
    private static PaymentService Payment(AppDbContext c, PaymentEvidenceTestGateway gateway) => new(new UserRepository(c),
        new SubscriptionRepository(c), new PaymentOrderRepository(c, new PaymentEvidenceTestClock(Now)), new PaymentOperationExecutor(c),
        gateway, new PaymentReconciliationService(new PaymentOrderRepository(c, new PaymentEvidenceTestClock(Now)),
            gateway, Settlement(c), new PaymentEvidenceTestClock(Now), NullLogger<PaymentReconciliationService>.Instance),
        new PaymentEvidenceTestClock(Now), NullLogger<PaymentService>.Instance);
    private static PaymentWebhookService Webhook(AppDbContext c, PaymentEvidenceTestGateway gateway) => new(gateway, Settlement(c),
        new PaymentEvidenceRepository(c), Options.Create(new PaymentEvidenceOptions()), new PaymentEvidenceTestClock(Now), NullLogger<PaymentWebhookService>.Instance);
}
