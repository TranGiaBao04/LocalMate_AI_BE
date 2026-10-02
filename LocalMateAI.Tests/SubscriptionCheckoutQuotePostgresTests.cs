using System.Data.Common;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Payments;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class SubscriptionCheckoutQuotePostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);

    private static PaymentService Service(AppDbContext c, Gateway gateway) => new(
        new UserRepository(c), new SubscriptionRepository(c), new PaymentOrderRepository(c),
        new PaymentOperationExecutor(c), gateway,
        new PaymentReconciliationService(new PaymentOrderRepository(c), gateway,
            PlanVersionFoundationPostgresTests.Settlement(c, new PlanVersionFoundationPostgresTests.Clock(Now)),
            new PlanVersionFoundationPostgresTests.Clock(Now), NullLogger<PaymentReconciliationService>.Instance),
        new PlanVersionFoundationPostgresTests.Clock(Now), NullLogger<PaymentService>.Instance);

    private static async Task<SubscriptionPeriod> PaidPeriod(AppDbContext c, Guid user,
        SubscriptionPlanVersion version, DateTime paidAt)
    {
        var order = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user, version.PlanId, version);
        var result = await PlanVersionFoundationPostgresTests.Settlement(c,
            new PlanVersionFoundationPostgresTests.Clock(paidAt))
            .ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true));
        Assert.Equal(PaymentSettlementStatus.Settled, result.Status);
        return await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == order.Id);
    }

    private static async Task<int[]> Counts(AppDbContext c) =>
        [await c.PaymentOrders.CountAsync(), await c.PaymentOrderCredits.CountAsync(),
         await c.PaymentOrderStatusHistories.CountAsync(), await c.SubscriptionPeriods.CountAsync(),
         await c.EmailOutboxMessages.CountAsync()];

    [Fact]
    public async Task NativeProjection_NoTracking_NoSensitiveOrderColumns_QuoteAndGateNeverWrite()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(seed);
        var v = await seed.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(seed, user.Id, v, Now.AddDays(-2));
        var pending = await PlanVersionFoundationPostgresTests.BoundOrderAsync(seed, user.Id,
            SubscriptionBaseline.PlanId(PlanCode.Membership),
            await seed.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.Membership)));
        var before = await Counts(seed);
        var capture = new Capture();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(capture).Options);
        var gateway = new Gateway();
        var quote = await Service(c, gateway).GetCheckoutQuoteAsync(user.Id, " membership ");
        Assert.Equal(PaymentIntentResultStatus.Success, quote.Status);
        Assert.Equal(49000, quote.Response!.Amount);
        Assert.Empty(c.ChangeTracker.Entries());
        Assert.All(capture.Sql, sql => { Assert.DoesNotContain("FOR UPDATE", sql); Assert.StartsWith("SELECT", sql); });
        var joined = Assert.Single(capture.Sql, s => s.Contains("LEFT JOIN \"PaymentOrders\""));
        Assert.DoesNotContain("CheckoutUrl", joined); Assert.DoesNotContain("QrCode", joined);
        Assert.Equal(PaymentIntentResultStatus.UpgradeCheckoutNotReady,
            (await Service(c, gateway).CheckoutAsync(user.Id, "Membership")).Status);
        Assert.Equal(before, await Counts(c));
        Assert.Equal(PaymentOrderStatus.Pending, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == pending.Id)).Status);
        Assert.Equal(0, gateway.Creates); Assert.Equal(0, gateway.Lookups);
    }

    [Fact]
    public async Task LegacyAggregateHasZeroMoney_ActualHistoricalInterval_NoCatalogDurationInference()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var legacy = new UserSubscription { UserId = user.Id, PlanCode = PlanCode.TripPass,
            StartsAt = Now.AddDays(-2), EndsAt = Now.AddDays(10) };
        c.UserSubscriptions.Add(legacy); await c.SaveChangesAsync();
        c.SubscriptionPeriods.Add(new() { UserId = user.Id, PlanId = SubscriptionBaseline.PlanId(PlanCode.TripPass),
            PlanVersionId = SubscriptionBaseline.VersionId(PlanCode.TripPass), LegacyUserSubscriptionId = legacy.Id,
            StartsAt = legacy.StartsAt, EndsAt = legacy.EndsAt });
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var q = (await Service(c, new Gateway()).GetCheckoutQuoteAsync(user.Id, "Membership")).Response!;
        var row = Assert.Single(q.Credits);
        Assert.Equal(0, row.CreditAmount); Assert.Equal(10, row.RemainingDays); Assert.Equal(59000, q.Amount);
        Assert.Empty(c.ChangeTracker.Entries());
    }

    [Fact]
    public async Task OldPurchasedNativeVersionRemainsAuthoritativeAfterPublication()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var (plan, old) = await PlanVersionFoundationPostgresTests.CustomAsync(c, "OLD_CUSTOM", 50, 49000, 7, null, 3);
        await PaidPeriod(c, user.Id, old, Now.AddDays(-2));
        await PlanVersionFoundationPostgresTests.PublishAsync(c, plan.Id, 2, 19000, 30, null, 3);
        var q = (await Service(c, new Gateway()).GetCheckoutQuoteAsync(user.Id, "Membership")).Response!;
        Assert.Equal(28000, Assert.Single(q.Credits).CreditAmount); Assert.Equal(31000, q.Amount);
    }

    [Fact]
    public async Task CurrentFutureAndMaskedSources_AreAllReadByOneEvidenceProjection()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(c, user.Id, trip, Now.AddDays(-2));
        await PaidPeriod(c, user.Id, trip, Now);
        var member = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.Membership));
        await PaidPeriod(c, user.Id, member, Now.AddDays(-2));
        var (target, _) = await PlanVersionFoundationPostgresTests.CustomAsync(c, "TOP_CUSTOM", 300, 90000, 30, null, null);
        var q = (await Service(c, new Gateway()).GetCheckoutQuoteAsync(user.Id, target.Code.ToLowerInvariant())).Response!;
        Assert.Equal(3, q.Credits.Count);
        Assert.Contains(q.Credits, x => x.PlanCode == "TripPass" && x.RemainingDays == 7 && x.CreditAmount == 19000);
    }

    [Theory]
    [InlineData(150, 9000, "Upgrade", 9000)]
    [InlineData(150, 15000, "Upgrade", 10000)]
    public async Task GenericCustomPriority_NotPrice(int priority, int price, string type, int amount)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(c, user.Id, trip, Now.AddDays(-2));
        var (target, _) = await PlanVersionFoundationPostgresTests.CustomAsync(c, "CHEAPER_CUSTOM", priority, price, 3, null, 5);
        var q = (await Service(c, new Gateway()).GetCheckoutQuoteAsync(user.Id, target.Code.ToLowerInvariant())).Response!;
        Assert.Equal(type, q.Type); Assert.Equal(amount, q.Amount);
        var (lower, _) = await PlanVersionFoundationPostgresTests.CustomAsync(c, "LOWER_CUSTOM", 50, 999999, 30, null, 5);
        Assert.Equal(PaymentIntentResultStatus.CoveredByHigherPlan,
            (await Service(c, new Gateway()).GetCheckoutQuoteAsync(user.Id, lower.Code)).Status);
    }

    [Fact]
    public async Task FutureTargetFailsClosed_BothQuoteAndCheckout()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await PaidPeriod(c, user.Id, trip, Now.AddDays(-2));
        var member = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.Membership));
        await PaidPeriod(c, user.Id, member, Now.AddDays(5));
        var before = await Counts(c);
        var gateway = new Gateway();
        Assert.Equal(PaymentIntentResultStatus.TargetPlanAlreadyScheduled,
            (await Service(c, gateway).GetCheckoutQuoteAsync(user.Id, "Membership")).Status);
        Assert.Equal(PaymentIntentResultStatus.TargetPlanAlreadyScheduled,
            (await Service(c, gateway).CheckoutAsync(user.Id, "Membership")).Status);
        Assert.Equal(before, await Counts(c)); Assert.Equal(0, gateway.Creates);
    }

    [Fact]
    public async Task PurchaseAndRenewalKeepFullPriceZeroCredit_NativeSnapshots()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var gateway = new Gateway();
        var service = Service(c, gateway);
        var purchase = await service.CheckoutAsync(user.Id, " trippass ");
        Assert.Equal(PaymentIntentResultStatus.Success, purchase.Status);
        Assert.Equal("Purchase", purchase.Response!.Type); Assert.Equal(0, purchase.Response.CreditAmount);
        var o = await c.PaymentOrders.SingleAsync();
        Assert.Equal(PlanVersionBinding.Native, o.PlanVersionBinding);
        await PlanVersionFoundationPostgresTests.Settlement(c, new PlanVersionFoundationPostgresTests.Clock(Now))
            .ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        var renew = await service.RenewAsync(user.Id);
        Assert.Equal(PaymentIntentResultStatus.Success, renew.Status);
        Assert.Equal("Renewal", renew.Response!.Type); Assert.Equal(19000, renew.Response.ListPrice);
        Assert.Equal(0, renew.Response.CreditAmount); Assert.Equal(2, gateway.Creates);
    }

    private sealed class Gateway : IPaymentGateway
    {
        public int Creates { get; private set; }
        public int Lookups { get; private set; }
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest request, CancellationToken ct = default)
        { Creates++; return Task.FromResult(PaymentLinkResult.Succeeded("https://example.invalid/quote-tests", "test-qr")); }
        public Task<PaymentGatewayOrderResult> GetPaymentAsync(long code, CancellationToken ct = default)
        { Lookups++; return Task.FromResult(PaymentGatewayOrderResult.Unavailable(code)); }
        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string raw, CancellationToken ct = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Invalid());
    }

    private sealed class Capture : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
