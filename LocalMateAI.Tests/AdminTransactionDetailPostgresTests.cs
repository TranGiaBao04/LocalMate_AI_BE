using System.Data;
using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class AdminTransactionDetailPostgresTests
{
    private static readonly DateTime Day = AdminTransactionPostgresTests.Day;
    private const string Raw = """{"bank":"RAW_BANK_SENTINEL","authorization":"RAW_HEADER_SENTINEL","note":"Đường Nguyễn Huệ"}""";

    [Theory]
    [InlineData(0, "Paid", "Native", "TRIP_PASS", false)]
    [InlineData(1, "Pending", "Native", "TRIP_PASS", false)]
    [InlineData(2, "Paid", "Native", "MEMBERSHIP", false)]
    [InlineData(3, "Failed", "Native", "MEMBERSHIP", false)]
    [InlineData(4, "Paid", "LegacyUnresolved", "TripPass", true)]
    [InlineData(5, "Expired", "LegacyUnresolved", null, true)]
    [InlineData(6, "Pending", "LegacyUnresolved", "EXPLORER", false)]
    [InlineData(7, "Paid", "LegacyUnresolved", "EXPLORER", false)]
    public async Task Detail_UsesWp5Projection_ForEveryCurrentAndLegacyState(int index, string status,
        string binding, string? code, bool noPlanName)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var orders = await AdminTransactionPostgresTests.SeedAsync(c);
        var service = AdminTransactionTests.Service(new AdminTransactionRepository(c));
        var order = orders[index];
        var list = (await service.GetTransactionsAsync(new() { Search = order.Id.ToString() })).Response!;
        var detail = (await service.GetDetailAsync(order.Id))!;
        Assert.Equal(Assert.Single(list.Items).Id, detail.Transaction.Id);
        using var listJson = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(list.Items)));
        using var detailJson = JsonDocument.Parse(JsonSerializer.Serialize(detail.Transaction));
        foreach (var property in listJson.RootElement.EnumerateObject())
            Assert.Equal(property.Value.GetRawText(), detailJson.RootElement.GetProperty(property.Name).GetRawText());
        Assert.Equal(status, detail.Transaction.Status);
        Assert.Equal(binding, detail.Transaction.PlanVersionBinding);
        Assert.Equal(code, detail.Transaction.PlanCode);
        Assert.Equal(noPlanName, detail.Transaction.PlanName is null);
        Assert.Equal(order.PlanId, detail.Transaction.PlanId);
        Assert.Equal(order.PlanVersionId, detail.Transaction.PlanVersionId);
        Assert.Equal(order.Amount, detail.Transaction.Amount);
        Assert.Equal((await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).UpdatedAt, detail.Transaction.UpdatedAt);
        Assert.Empty(detail.StatusHistory);
        Assert.Empty(detail.WebhookReceipts);
        Assert.Empty(c.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnknownGuid_ReadsOnlyTransaction_WithoutLeakingEvidence()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using (var setup = db.Context()) await AdminTransactionPostgresTests.SeedAsync(setup);
        var capture = new Capture();
        await using var c = Context(db, capture);
        Assert.Null(await new AdminTransactionRepository(c).GetDetailAsync(Guid.NewGuid()));
        Assert.Single(capture.Reads);
        Assert.Contains("PaymentOrders", capture.Reads[0]);
    }

    [Theory]
    [InlineData(null, PaymentOrderStatus.Pending, PaymentStatusChangeSource.Checkout, "order_created")]
    [InlineData(PaymentOrderStatus.Pending, PaymentOrderStatus.Failed, PaymentStatusChangeSource.Webhook, "amount_mismatch")]
    [InlineData(PaymentOrderStatus.Failed, PaymentOrderStatus.Paid, PaymentStatusChangeSource.Webhook, "verified_success")]
    [InlineData(PaymentOrderStatus.Expired, PaymentOrderStatus.Paid, PaymentStatusChangeSource.Webhook, "verified_success")]
    [InlineData(PaymentOrderStatus.Pending, PaymentOrderStatus.Expired, PaymentStatusChangeSource.LocalExpiration, "local_expired")]
    [InlineData(PaymentOrderStatus.Pending, PaymentOrderStatus.Paid, PaymentStatusChangeSource.ProviderLookup, "provider_paid")]
    public async Task History_PreservesNullableStatusSourceReasonActorAndReceipt(PaymentOrderStatus? from,
        PaymentOrderStatus to, PaymentStatusChangeSource source, string reason)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = (await AdminTransactionPostgresTests.SeedAsync(c))[0];
        var receipt = Receipt(order);
        c.PaymentWebhookReceipts.Add(receipt);
        var history = new PaymentOrderStatusHistory
        {
            PaymentOrderId = order.Id, FromStatus = from, ToStatus = to, Source = source, ReasonCode = reason,
            OccurredAt = Day, ActorUserId = order.UserId, WebhookReceiptId = receipt.Id
        };
        c.PaymentOrderStatusHistories.Add(history);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var row = Assert.Single((await new AdminTransactionRepository(c).GetDetailAsync(order.Id))!.StatusHistory);
        Assert.Equal(history.Id, row.Id);
        Assert.Equal(from?.ToString(), row.FromStatus);
        Assert.Equal(to.ToString(), row.ToStatus);
        Assert.Equal(source.ToString(), row.Source);
        Assert.Equal(reason, row.ReasonCode);
        Assert.Equal(Day, row.OccurredAt);
        Assert.Equal(order.UserId, row.ActorUserId);
        Assert.Equal(receipt.Id, row.WebhookReceiptId);
    }

    [Fact]
    public async Task History_ChronologicalWithIdTieBreaker_NoReasonInterpretationOrOtherOrderRows()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var orders = await AdminTransactionPostgresTests.SeedAsync(c);
        var history = new[]
        {
            History(orders[0], 1, null, PaymentOrderStatus.Pending, Day),
            History(orders[0], 2, PaymentOrderStatus.Pending, PaymentOrderStatus.Failed, Day.AddMinutes(1)),
            History(orders[0], 3, PaymentOrderStatus.Failed, PaymentOrderStatus.Paid, Day.AddMinutes(1)),
            History(orders[1], 4, null, PaymentOrderStatus.Pending, Day)
        };
        c.PaymentOrderStatusHistories.AddRange(history.Reverse());
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var rows = (await new AdminTransactionRepository(c).GetDetailAsync(orders[0].Id))!.StatusHistory;
        Assert.Equal(history.Take(3).Select(h => h.Id), rows.Select(h => h.Id));
        Assert.All(rows, h => Assert.Equal("stored_reason_v0", h.ReasonCode));
        Assert.All(rows, h => { Assert.Null(h.ActorUserId); Assert.Null(h.WebhookReceiptId); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipts_NormalizedDuplicateRowsNewestFirst_RetainedAndPurgedMetadata(bool purged)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var orders = await AdminTransactionPostgresTests.SeedAsync(c);
        var receipts = Enumerable.Range(1, 3).Select(i => Receipt(orders[0], i, purged)).ToArray();
        c.PaymentWebhookReceipts.AddRange(receipts.Reverse());
        c.PaymentWebhookReceipts.Add(Receipt(orders[1], 4, purged));
        var unknown = Receipt(orders[0], 5, purged);
        unknown.PaymentOrderId = null;
        c.PaymentWebhookReceipts.Add(unknown);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var detail = (await new AdminTransactionRepository(c).GetDetailAsync(orders[0].Id))!;
        Assert.Equal(new[] { receipts[2].Id, receipts[1].Id, receipts[0].Id }, detail.WebhookReceipts.Select(r => r.Id));
        Assert.All(detail.WebhookReceipts, r =>
        {
            Assert.Equal(orders[0].ProviderOrderCode, r.ProviderOrderCode);
            Assert.Equal(orders[0].Amount, r.Amount);
            Assert.True(r.IsSuccessful);
            Assert.Equal(!purged, r.HasRawPayload);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Raw))), r.RawPayloadSha256);
            var original = receipts.Single(x => x.Id == r.Id);
            Assert.Equal(original.ReceivedAt, r.ReceivedAt);
            Assert.Equal(original.RawPayloadRetainUntil, r.RawPayloadRetainUntil);
            Assert.Equal(original.RawPayloadPurgedAt, r.RawPayloadPurgedAt);
        });
        using var host = new AdminTransactionsHttpTests.Host(AdminTransactionTests.Service(new AdminTransactionRepository(c)));
        host.Authenticate();
        var response = await host.Client.GetAsync("/api/admin/transactions/" + orders[0].Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { Raw, "RAW_BANK_SENTINEL", "RAW_HEADER_SENTINEL", "PRIVATE_CHECKOUT_SENTINEL",
                     "PRIVATE_QR_SENTINEL", "PRIVATE_HASH_SENTINEL" })
            Assert.DoesNotContain(secret, body);
        using var json = JsonDocument.Parse(body);
        AdminTransactionDetailHttpTests.AssertSafe(json.RootElement);
    }

    [Fact]
    public async Task Sql_SelectsOnlySafeColumns_BoundedQueries_NoTrackingWritesOrRowLocks()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        PaymentOrder order;
        await using (var setup = db.Context())
        {
            order = (await AdminTransactionPostgresTests.SeedAsync(setup))[0];
            setup.PaymentWebhookReceipts.Add(Receipt(order));
            setup.PaymentOrderStatusHistories.Add(History(order, 1, null, PaymentOrderStatus.Pending, Day));
            await setup.SaveChangesAsync();
        }
        var capture = new Capture { InspectReadOnly = true };
        await using var c = Context(db, capture);
        var before = await Counts(db);
        var detail = (await new AdminTransactionRepository(c).GetDetailAsync(order.Id))!;
        Assert.True(Assert.Single(detail.WebhookReceipts).HasRawPayload);
        Assert.Equal(before, await Counts(db));
        Assert.Equal(8, capture.Reads.Count);
        Assert.Equal(new[] { "SET TRANSACTION READ ONLY" }, capture.Writes);
        Assert.Equal("on", capture.ServerReadOnly);
        Assert.All(capture.IsolationLevels, level => Assert.Equal(IsolationLevel.RepeatableRead, level));
        Assert.All(capture.Reads, sql =>
        {
            foreach (var forbidden in new[] { "CheckoutUrl", "QrCode", "PasswordHash", "FOR UPDATE", "FOR SHARE", "INSERT ", "UPDATE ", "DELETE " })
                Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        });
        var receiptSql = capture.Reads.Single(sql => sql.Contains("PaymentWebhookReceipts"));
        Assert.Contains("\"RawPayload\" IS NOT NULL", receiptSql);
        Assert.DoesNotContain("PaymentOrderId", receiptSql[..receiptSql.IndexOf("FROM", StringComparison.Ordinal)]);
        Assert.All(capture.Columns, columns => Assert.DoesNotContain("RawPayload", columns, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(c.ChangeTracker.Entries());
        Assert.False(c.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("PaymentOrders")]
    [InlineData("PaymentOrderStatusHistories")]
    public async Task RepeatableRead_ConcurrentVerifiedReceiptAndActualSettlement_CannotMixStatements(string afterTable)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        PaymentOrder order;
        await using (var setup = db.Context()) order = (await AdminTransactionPostgresTests.SeedAsync(setup))[1];
        var capture = new Capture
        {
            AfterTable = afterTable,
            AfterRead = async () =>
            {
                await using var writer = db.Context();
                await writer.Database.ExecuteSqlRawAsync("SET lock_timeout = '2s'");
                var receipt = Receipt(order);
                await new PaymentEvidenceRepository(writer).SaveVerifiedReceiptAsync(receipt);
                var clock = new PaymentEvidenceTestClock(Day.AddHours(2));
                var settlement = new PaymentSettlementService(new PaymentSettlementExecutor(writer, clock),
                    new SubscriptionRepository(writer), new UserRepository(writer), new EmailOutboxRepository(writer),
                    clock, NullLogger<PaymentSettlementService>.Instance);
                var result = await settlement.ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true),
                    new PaymentTransitionContext(PaymentStatusChangeSource.Webhook, WebhookReceiptId: receipt.Id));
                Assert.Equal(PaymentSettlementStatus.Settled, result.Status);
            }
        };
        await using var reader = Context(db, capture);
        var snapshot = (await new AdminTransactionRepository(reader).GetDetailAsync(order.Id))!;
        Assert.True(capture.Injected);
        Assert.Equal("Pending", snapshot.Transaction.Status);
        Assert.Null(snapshot.Transaction.PaidAt);
        Assert.Empty(snapshot.StatusHistory);
        Assert.Empty(snapshot.WebhookReceipts);
        await using var fresh = db.Context();
        var current = (await new AdminTransactionRepository(fresh).GetDetailAsync(order.Id))!;
        Assert.Equal("Paid", current.Transaction.Status);
        var transition = Assert.Single(current.StatusHistory);
        Assert.Equal("Pending", transition.FromStatus);
        Assert.Equal("Paid", transition.ToStatus);
        Assert.Equal(Assert.Single(current.WebhookReceipts).Id, transition.WebhookReceiptId);
        Assert.Equal(1, await fresh.SubscriptionPeriods.CountAsync(p => p.SourcePaymentOrderId == order.Id));
        Assert.Equal(8, capture.Reads.Count);
    }

    [Fact]
    public async Task ReceiptBeforeSettlement_IsLegitimatePendingSnapshot_NoRepairOrMutation()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = (await AdminTransactionPostgresTests.SeedAsync(c))[1];
        await new PaymentEvidenceRepository(c).SaveVerifiedReceiptAsync(Receipt(order));
        c.ChangeTracker.Clear();
        var before = await Counts(db);
        var detail = (await new AdminTransactionRepository(c).GetDetailAsync(order.Id))!;
        Assert.Equal("Pending", detail.Transaction.Status);
        Assert.Single(detail.WebhookReceipts);
        Assert.Empty(detail.StatusHistory);
        Assert.Equal(before, await Counts(db));
        Assert.Equal(0, await c.SubscriptionPeriods.CountAsync(p => p.SourcePaymentOrderId == order.Id));
        Assert.Empty(c.ChangeTracker.Entries());
    }

    private static PaymentOrderStatusHistory History(PaymentOrder order, int index, PaymentOrderStatus? from,
        PaymentOrderStatus to, DateTime at) => new()
    {
        Id = Guid.Parse($"10000000-0000-0000-0000-{index:D12}"), PaymentOrderId = order.Id,
        FromStatus = from, ToStatus = to, Source = PaymentStatusChangeSource.Webhook,
        ReasonCode = "stored_reason_v0", OccurredAt = at
    };

    private static PaymentWebhookReceipt Receipt(PaymentOrder order, int index = 1, bool purged = false)
    {
        var received = DateTime.UtcNow.AddDays(-60).Date.AddMinutes(index == 1 ? 0 : 1);
        return new()
        {
            Id = Guid.Parse($"20000000-0000-0000-0000-{index:D12}"), PaymentOrderId = order.Id,
            ProviderOrderCode = order.ProviderOrderCode, Amount = order.Amount, IsSuccessful = true,
            ReceivedAt = received, RawPayload = purged ? null : Raw,
            RawPayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Raw))),
            RawPayloadRetainUntil = received.AddDays(30), RawPayloadPurgedAt = purged ? received.AddDays(31) : null
        };
    }

    private static AppDbContext Context(IsolatedPlanDatabase db, Capture capture) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Connection, o => o.UseNetTopologySuite())
            .AddInterceptors(capture).Options);

    private static async Task<(int Orders, int History, int Receipts, int Periods, int Emails)> Counts(IsolatedPlanDatabase db)
    {
        await using var c = db.Context();
        return (await c.PaymentOrders.CountAsync(), await c.PaymentOrderStatusHistories.CountAsync(),
            await c.PaymentWebhookReceipts.CountAsync(), await c.SubscriptionPeriods.CountAsync(), await c.EmailOutboxMessages.CountAsync());
    }

    private sealed class Capture : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public List<string> Writes { get; } = [];
        public List<string[]> Columns { get; } = [];
        public List<IsolationLevel> IsolationLevels { get; } = [];
        public bool InspectReadOnly { get; init; }
        public string? ServerReadOnly { get; private set; }
        public string? AfterTable { get; init; }
        public Func<Task>? AfterRead { get; init; }
        public bool Injected { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads.Add(command.CommandText);
            IsolationLevels.Add(command.Transaction!.IsolationLevel);
            if (InspectReadOnly && ServerReadOnly is null)
            {
                await using var inspect = command.Connection!.CreateCommand();
                inspect.Transaction = command.Transaction;
                inspect.CommandText = "SHOW transaction_read_only";
                ServerReadOnly = (string)(await inspect.ExecuteScalarAsync(cancellationToken))!;
            }
            return result;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Columns.Add(Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray());
            if (!Injected && AfterRead is not null && command.CommandText.Contains($"""FROM "{AfterTable}" """, StringComparison.Ordinal))
            {
                Injected = true;
                await AfterRead();
            }
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Writes.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
