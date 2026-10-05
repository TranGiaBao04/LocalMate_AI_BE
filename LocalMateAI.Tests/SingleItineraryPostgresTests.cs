using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.ItineraryPurchases;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class SingleItineraryPostgresTests
{
    internal static readonly DateTime Now = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
    internal static readonly PaymentEvidenceTestClock Clock = new(Now);

    internal static PaymentSettlementService Settlement(AppDbContext c) => new(new PaymentSettlementExecutor(c, Clock),
        new SubscriptionRepository(c), new UserRepository(c), new EmailOutboxRepository(c), Clock,
        NullLogger<PaymentSettlementService>.Instance, new SingleItineraryRepository(c));
    internal static FinalizeTripCommand Finalize(AppDbContext c) => new(new TripRepository(c), new SubscriptionRepository(c),
        new TripFinalizeQuotaExecutor(c), new UserRepository(c), new EmailOutboxRepository(c), Clock, new SingleItineraryRepository(c));
    internal static ItineraryPurchaseService Purchases(AppDbContext c, Gateway g) => new(new SingleItineraryRepository(c),
        new PaymentOrderRepository(c, Clock), new PaymentOperationExecutor(c), new PaymentSettlementExecutor(c, Clock),
        g, new PaymentReconciliationService(new PaymentOrderRepository(c, Clock), g, Settlement(c), Clock,
            NullLogger<PaymentReconciliationService>.Instance), new UserRepository(c), new SubscriptionRepository(c),
        new TripRepository(c), Clock);

    internal static async Task<PaymentOrder> Order(AppDbContext c, Guid user)
    {
        var o = new PaymentOrder { UserId = user, ProductKind = PaymentProductKind.SingleItinerary,
            PlanVersionBinding = null, SingleItineraryProductVersionId = SingleItineraryBaseline.VersionId,
            CheckoutAttemptId = Guid.NewGuid(), Amount = 29000, Type = PaymentOrderType.Purchase,
            ExpiresAt = Now.AddMinutes(15) };
        await new PaymentOrderRepository(c, Clock).AddAsync(o);
        return o;
    }
    internal static async Task<SingleItineraryEntitlement> Grant(AppDbContext c, Guid user)
    {
        var o = await Order(c, user);
        Assert.Equal(PaymentSettlementStatus.Settled,
            (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, 29000, true))).Status);
        return await c.SingleItineraryEntitlements.AsNoTracking().SingleAsync(e => e.SourcePaymentOrderId == o.Id);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Pending)]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Expired)]
    public async Task PaidAndLatePayment_GrantsUnboundOriginalTerms(PaymentOrderStatus before)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var o = await Order(c, u.Id);
        if (before != PaymentOrderStatus.Pending)
            await new PaymentOrderRepository(c).TransitionStatusAsync(o, before, new(PaymentStatusChangeSource.LocalExpiration), Now);
        c.SingleItineraryProductVersions.Add(new() { Id = Guid.NewGuid(), VersionNumber = 2, Price = 39000, CreatedAt = Now, PublishedAt = Now });
        await c.SaveChangesAsync();
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, 29000, true))).Status);
        var e = await c.SingleItineraryEntitlements.SingleAsync();
        Assert.Equal(SingleItineraryBaseline.VersionId, e.SingleItineraryProductVersionId);
        Assert.Null(e.ConsumedAt); Assert.Null(e.ConsumedTripId); Assert.Equal(Now, e.GrantedAt);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync()); Assert.Empty(await c.UsageEvents.ToListAsync());
        Assert.Equal(1, await c.EmailOutboxMessages.CountAsync());
        Assert.Equal(1, await c.PaymentOrderStatusHistories.CountAsync(h => h.PaymentOrderId == o.Id && h.ToStatus == PaymentOrderStatus.Paid));
        Assert.Equal(PaymentSettlementStatus.AlreadyPaid, (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, 29000, true))).Status);
        Assert.Equal(1, await c.SingleItineraryEntitlements.CountAsync());
    }

    [Fact]
    public async Task Capacity_ConsumeRetryDeleteFork_NeverRestoresOrChargesNormal()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var normal = await PlanVersionFoundationPostgresTests.TripAsync(c, u.Id);
        Assert.Equal(FinalizeTripResultStatus.Success, (await Finalize(c).ExecuteAsync(u.Id, normal.Id)).Status);
        var purchased = await PlanVersionFoundationPostgresTests.TripAsync(c, u.Id);
        var e = await Grant(c, u.Id);
        var request = new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = e.Id };
        var first = await Finalize(c).ExecuteAsync(u.Id, purchased.Id, request);
        Assert.Equal(FinalizeTripResultStatus.Success, first.Status);
        Assert.Equal("SingleEntitlement", first.Response!.FundingSource);
        Assert.Equal(1, await new TripRepository(c).CountNormalFinalizedByUserAsync(u.Id));
        Assert.Equal(2, await new TripRepository(c).CountFinalizedByUserAsync(u.Id));
        Assert.Equal(FinalizeTripResultStatus.Success, (await Finalize(c).ExecuteAsync(u.Id, purchased.Id, request)).Status);
        var secondPurchased = await PlanVersionFoundationPostgresTests.TripAsync(c, u.Id);
        var secondGrant = await Grant(c, u.Id);
        Assert.Equal(FinalizeTripResultStatus.Success, (await Finalize(c).ExecuteAsync(u.Id, secondPurchased.Id,
            new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = secondGrant.Id })).Status);
        var me = (await new SubscriptionService(new UserRepository(c), new SubscriptionRepository(c),
            new UsageEventRepository(c), new TripRepository(c), Clock,
            new LlmCallLogRepository(c), new FakeSystemSettingProvider()).GetMySubscriptionAsync(u.Id))!;
        Assert.Equal(1, me.SavedTrips.Used); Assert.Equal(1, me.SavedTrips.Limit);
        var availability = (await Purchases(c, new()).GetAvailabilityAsync(u.Id)).Response!;
        Assert.Equal(1, availability.NormalSavedTripsUsed); Assert.False(availability.NormalFinalizeAvailable);
        Assert.Equal(0, availability.UnusedEntitlementCount); Assert.True(availability.PurchaseAllowed);
        Assert.Equal(3, await new TripRepository(c).CountFinalizedByUserAsync(u.Id));
        Assert.Empty(await c.UsageEvents.ToListAsync());
        var another = await PlanVersionFoundationPostgresTests.TripAsync(c, u.Id);
        Assert.Equal(FinalizeTripResultStatus.EntitlementConsumed, (await Finalize(c).ExecuteAsync(u.Id, another.Id, request)).Status);
        Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, (await Finalize(c).ExecuteAsync(u.Id, another.Id)).Status);
        var fork = (await new ForkTripCommand(new TripRepository(c)).ExecuteAsync(u.Id, purchased.Id)).Response!;
        Assert.NotEqual(purchased.Id, fork.TripId);
        Assert.Equal(FinalizeTripResultStatus.SavedTripQuotaExceeded, (await Finalize(c).ExecuteAsync(u.Id, fork.TripId)).Status);
        await new TripDeletionService(new UserRepository(c), new TripRepository(c), new TripFinalizeQuotaExecutor(c)).DeleteAsync(u.Id, purchased.Id);
        Assert.NotNull((await c.SingleItineraryEntitlements.AsNoTracking().SingleAsync(x => x.Id == e.Id)).ConsumedAt);
        Assert.Equal(1, await new TripRepository(c).CountNormalFinalizedByUserAsync(u.Id));
        Assert.Equal(5, await c.EmailOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task TwoDeliberatePurchases_OneRetry_ProviderOutsideUserLock()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var g = new Gateway { Probe = async () =>
        {
            await using var other = db.Context();
            Assert.Equal(1, await other.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={u.Id} FOR UPDATE NOWAIT""").SingleAsync());
        }};
        var service = Purchases(c, g); var attempt = Guid.NewGuid();
        var a = await service.CheckoutAsync(u.Id, attempt);
        var retry = await service.CheckoutAsync(u.Id, attempt);
        Assert.Equal(a.Response!.OrderId, retry.Response!.OrderId);
        Assert.Equal(1, g.Creates);
        var b = await service.CheckoutAsync(u.Id, Guid.NewGuid()); Assert.Equal(2, g.Creates);
        foreach (var row in await c.PaymentOrders.AsNoTracking().ToListAsync())
            await Settlement(c).ApplyVerifiedPaymentAsync(new(row.ProviderOrderCode, row.Amount, true));
        Assert.Equal(2, await c.SingleItineraryEntitlements.CountAsync());
        Assert.NotEqual(a.Response.OrderId, b.Response!.OrderId);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unusable")]
    [InlineData("timeout")]
    public async Task CheckoutFailure_RetryReusesOrder_ValidLatePaymentStillGrants(string failure)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c); var attempt = Guid.NewGuid();
        var g = new Gateway { Link = failure == "unusable" ? PaymentLinkResult.Succeeded("", "") : PaymentLinkResult.Unavailable(),
            Probe = failure == "timeout" ? () => Task.FromException(new TimeoutException()) : null };
        var service = Purchases(c, g);
        Assert.Equal(ItineraryPurchaseStatus.ProviderUnavailable, (await service.CheckoutAsync(u.Id, attempt)).Status);
        var retry = (await service.CheckoutAsync(u.Id, attempt)).Response!;
        Assert.Equal("Failed", retry.Status); Assert.Equal(1, g.Creates);
        Assert.Equal(1, await c.PaymentOrders.CountAsync()); Assert.Null(retry.Entitlement);
        Assert.Empty(await c.SingleItineraryEntitlements.ToListAsync());
        Assert.Equal(1, await c.PaymentOrderStatusHistories.CountAsync(h => h.ToStatus == PaymentOrderStatus.Failed));
        var recovered = (await service.GetOrderAsync(u.Id, retry.OrderId)).Response!;
        Assert.Equal("Paid", recovered.Status); Assert.True(recovered.Entitlement!.Available);
        Assert.Null(recovered.CheckoutUrl); Assert.Null(recovered.QrCode);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
    }

    [Fact]
    public async Task InterruptedCheckout_SameAttemptDoesNotCreateAgain_PaidLookupRecoversEvidence()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c); var attempt = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var g = new Gateway { Probe = () => { cancellation.Cancel(); return Task.FromCanceled(cancellation.Token); } };
        var service = Purchases(c, g);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CheckoutAsync(u.Id, attempt, cancellation.Token));
        var retry = await service.CheckoutAsync(u.Id, attempt);
        Assert.Equal(ItineraryPurchaseStatus.Accepted, retry.Status); Assert.Equal(1, g.Creates);
        Assert.Equal(1, await c.PaymentOrders.CountAsync());
        var recovered = (await service.GetOrderAsync(u.Id, retry.Response!.OrderId)).Response!;
        Assert.Equal("Paid", recovered.Status); Assert.True(recovered.Entitlement!.Available);
    }

    [Theory]
    [InlineData("reopen")]
    [InlineData("retarget")]
    [InlineData("trip_owner")]
    [InlineData("trip_state")]
    public async Task ConsumedDatabaseEvidence_CannotBeReopenedOrRetargeted(string mutation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PlanVersionFoundationPostgresTests.TripAsync(c, u.Id); var e = await Grant(c, u.Id);
        await Finalize(c).ExecuteAsync(u.Id, trip.Id, new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = e.Id });
        var statement = mutation switch
        {
            "reopen" => """UPDATE "SingleItineraryEntitlements" SET "ConsumedAt"=NULL,"ConsumedTripId"=NULL""",
            "retarget" => $"""UPDATE "SingleItineraryEntitlements" SET "ConsumedTripId"='{Guid.NewGuid()}'""",
            "trip_owner" => $"""UPDATE "Trips" SET "UserId"='{Guid.NewGuid()}' WHERE "Id"='{trip.Id}'""",
            _ => $"""UPDATE "Trips" SET "Status"='Draft' WHERE "Id"='{trip.Id}'"""
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(statement));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(trip.Id, (await c.SingleItineraryEntitlements.AsNoTracking().SingleAsync()).ConsumedTripId);
    }

    [Fact]
    public async Task OwnedApiService_RejectsForeignOrderAndEntitlement()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var owner = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var foreign = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var e = await Grant(c, owner.Id);
        Assert.Equal(ItineraryPurchaseStatus.NotFound, (await Purchases(c, new()).GetOrderAsync(foreign.Id, e.SourcePaymentOrderId)).Status);
        var trip = await PlanVersionFoundationPostgresTests.TripAsync(c, foreign.Id);
        Assert.Equal(FinalizeTripResultStatus.EntitlementNotFound, (await Finalize(c).ExecuteAsync(foreign.Id, trip.Id,
            new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = e.Id })).Status);
        Assert.Equal(FinalizeTripResultStatus.TripNotFound, (await Finalize(c).ExecuteAsync(foreign.Id,
            (await PlanVersionFoundationPostgresTests.TripAsync(c, owner.Id)).Id,
            new FinalizeTripRequest { FundingSource = "SingleEntitlement", EntitlementId = e.Id })).Status);
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("history")]
    [InlineData("outbox")]
    public async Task SettlementRollback_IsAtomic(string target)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c); var o = await Order(c, u.Id);
        var table = target == "grant" ? "SingleItineraryEntitlements" : target == "history" ? "PaymentOrderStatusHistories" : "EmailOutboxMessages";
        // The identifier is selected exclusively from the three fixed test tables above.
        var statement = $"""CREATE FUNCTION test_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Controlled failure'; END $$; CREATE TRIGGER fail BEFORE INSERT ON "{table}" FOR EACH ROW EXECUTE FUNCTION test_fail();""";
        await c.Database.ExecuteSqlRawAsync(statement);
        await Assert.ThrowsAnyAsync<Exception>(() => Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, 29000, true)));
        c.ChangeTracker.Clear();
        Assert.Equal(PaymentOrderStatus.Pending, (await c.PaymentOrders.SingleAsync()).Status);
        Assert.Empty(await c.SingleItineraryEntitlements.ToListAsync());
        Assert.Empty(await c.EmailOutboxMessages.ToListAsync());
        Assert.Single(await c.PaymentOrderStatusHistories.ToListAsync());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("product")]
    [InlineData("version")]
    [InlineData("owner")]
    [InlineData("grant_delete")]
    [InlineData("grant_truncate")]
    [InlineData("contract_update")]
    [InlineData("contract_delete")]
    [InlineData("contract_truncate")]
    public async Task DatabaseGuards_ProtectPurchaseEvidence(string mutation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c); var e = await Grant(c, u.Id);
        var sql = mutation switch
        {
            "amount" => $"UPDATE \"PaymentOrders\" SET \"Amount\"=1 WHERE \"Id\"='{e.SourcePaymentOrderId}'",
            "product" => $"UPDATE \"PaymentOrders\" SET \"ProductKind\"='SubscriptionPlan' WHERE \"Id\"='{e.SourcePaymentOrderId}'",
            "version" => $"UPDATE \"SingleItineraryEntitlements\" SET \"SingleItineraryProductVersionId\"='{Guid.NewGuid()}'",
            "owner" => $"UPDATE \"SingleItineraryEntitlements\" SET \"UserId\"='{Guid.NewGuid()}'",
            "grant_delete" => "DELETE FROM \"SingleItineraryEntitlements\"",
            "grant_truncate" => "TRUNCATE \"SingleItineraryEntitlements\" CASCADE",
            "contract_update" => "UPDATE \"SingleItineraryProductVersions\" SET \"Price\"=1",
            "contract_delete" => "DELETE FROM \"SingleItineraryProductVersions\"",
            _ => "TRUNCATE \"SingleItineraryProductVersions\" CASCADE"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task AmountMismatch_NoGrant_AndPaidRequiresGrant()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c); var o = await Order(c, u.Id);
        Assert.Equal(PaymentSettlementStatus.AmountMismatch, (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, 1, true))).Status);
        Assert.Empty(await c.SingleItineraryEntitlements.ToListAsync());
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "PaymentOrders" SET "Status"='Paid',"PaidAt"={Now} WHERE "Id"={o.Id}"""));
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, 29000, true))).Status);
    }

    [Fact]
    public async Task Upgrade_PreservesHistoricalMoney_AndFreshCatalog()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: "20261001041525_AddEntitlementRepairAudits");
        await using var c = db.Context(); var u = await PlanVersionFoundationPostgresTests.UserAsync(c); var id = Guid.NewGuid();
        await c.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PaymentOrders" ("Id","UserId","PlanCode","Type","Amount","Status","ExpiresAt","PaidAt","CreatedAt","UpdatedAt","PlanVersionBinding")
            VALUES ({id},{u.Id},'TripPass','Purchase',49000,'Paid',{Now},{Now},{Now},{Now},'LegacyUnresolved')
            """);
        await c.Database.MigrateAsync();
        var order = await c.PaymentOrders.SingleAsync();
        Assert.Equal(49000, order.Amount); Assert.Equal(Now, order.PaidAt); Assert.Equal(PaymentOrderStatus.Paid, order.Status);
        Assert.Equal(PaymentProductKind.SubscriptionPlan, order.ProductKind);
        Assert.Null(order.SingleItineraryProductVersionId);
        Assert.Equal(29000m, (await c.SingleItineraryProductVersions.SingleAsync()).Price);
        Assert.Empty(await c.SingleItineraryEntitlements.ToListAsync());
        Assert.True(await c.Roles.AnyAsync()); Assert.Equal(3, await c.SubscriptionPlans.CountAsync());
        Assert.False(c.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task AdminDetailAndRevenue_AreProductAware_RepairCannotGrantSubscription()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c); var e = await Grant(c, u.Id);
        var repo = new AdminTransactionRepository(c, Clock);
        var detail = (await repo.GetDetailAsync(e.SourcePaymentOrderId))!;
        Assert.Equal("SingleItinerary", detail.Transaction.ProductKind);
        Assert.Null(detail.Transaction.PlanCode); Assert.Null(detail.Transaction.PlanId);
        Assert.Null(detail.Transaction.PlanVersionBinding);
        Assert.Equal(e.Id, detail.SingleItineraryEntitlement!.EntitlementId);
        Assert.Equal("NotApplicable", detail.Entitlement!.GrantStatus);
        Assert.Equal(29000m, (await repo.GetSummaryAsync(new(null, null, null, null, null))).GrossRevenue);
        var repaired = await new EntitlementRepairExecutor(c, Clock).ExecuteAsync(e.SourcePaymentOrderId, u.Id, "check");
        Assert.Equal(EntitlementRepairOutcome.NotEligible, repaired!.Result);
        Assert.Equal("product_not_applicable", repaired.DecisionCode);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
    }

    internal sealed class Gateway : IPaymentGateway
    {
        public int Creates; public Func<Task>? Probe; public PaymentGatewayOrderStatus Status = PaymentGatewayOrderStatus.Paid;
        public PaymentLinkResult Link = PaymentLinkResult.Succeeded("https://example.invalid/checkout", "test-qr");
        public async Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest request, CancellationToken ct = default)
        { Interlocked.Increment(ref Creates); if (Probe is not null) await Probe(); return Link; }
        public Task<PaymentGatewayOrderResult> GetPaymentAsync(long code, CancellationToken ct = default) => Task.FromResult(new PaymentGatewayOrderResult(true, code, 29000m, Status));
        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string raw, CancellationToken ct = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Valid(new(long.Parse(raw), 29000, true)));
    }
}
