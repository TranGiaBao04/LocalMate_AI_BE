using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Domain.Services;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeSettlementPostgresTests
{
    private static readonly DateTime Now = SubscriptionUpgradeReservationPostgresTests.Now;
    private static readonly TimeProvider Clock = SubscriptionUpgradeReservationPostgresTests.Clock;
    private static PaymentSettlementService Settlement(AppDbContext c) => new(new PaymentSettlementExecutor(c, Clock),
        new SubscriptionRepository(c), new UserRepository(c), new EmailOutboxRepository(c), Clock,
        NullLogger<PaymentSettlementService>.Instance, new SingleItineraryRepository(c), new PaymentCreditRepository(c, Clock));
    internal static PaymentService Checkout(AppDbContext c, Gateway g) => new(new UserRepository(c),
        new SubscriptionRepository(c), new PaymentOrderRepository(c, Clock), new PaymentOperationExecutor(c), g,
        new PaymentReconciliationService(new PaymentOrderRepository(c, Clock), g, Settlement(c), Clock,
            NullLogger<PaymentReconciliationService>.Instance), Clock, NullLogger<PaymentService>.Instance,
        new SubscriptionUpgradeReservationService(new SubscriptionRepository(c), new PaymentOrderRepository(c, Clock),
            new PaymentCreditRepository(c, Clock), new PaymentOperationExecutor(c), g, Clock), Settlement(c));

    internal sealed class Gateway : IPaymentGateway
    {
        public List<PaymentLinkRequest> Requests { get; } = [];
        public int Lookups;
        public PaymentGatewayOrderStatus ProviderStatus = PaymentGatewayOrderStatus.Cancelled;
        public decimal Amount;
        public bool Unavailable;
        public Func<PaymentLinkRequest, Task>? BeforeCreate;
        public VerifiedPaymentNotification? Notification;
        public async Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest r, CancellationToken ct = default)
        {
            lock (Requests) Requests.Add(r);
            if (BeforeCreate is not null) await BeforeCreate(r);
            if (Unavailable) throw new TimeoutException("controlled fake timeout");
            return PaymentLinkResult.Succeeded("https://example.invalid/ups4", "fake-upgrade-qr");
        }
        public Task<PaymentGatewayOrderResult> GetPaymentAsync(long code, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Lookups);
            return Task.FromResult(Unavailable ? PaymentGatewayOrderResult.Unavailable(code)
                : new PaymentGatewayOrderResult(true, code, Amount, ProviderStatus)
                { RequestedAmount = Amount, AmountPaid = ProviderStatus == PaymentGatewayOrderStatus.Cancelled ? 0 : Amount,
                    AmountRemaining = ProviderStatus == PaymentGatewayOrderStatus.Cancelled ? Amount : 0 });
        }
        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string raw, CancellationToken ct = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Valid(Notification!));
    }

    private static PaymentWebhookService Webhook(AppDbContext c, Gateway g) => new(g, Settlement(c),
        new PaymentEvidenceRepository(c), Options.Create(new PaymentEvidenceOptions()), Clock,
        NullLogger<PaymentWebhookService>.Instance);

    private static async Task AssertPaid(AppDbContext c, PaymentOrder o)
    {
        var order = await c.PaymentOrders.AsNoTracking().SingleAsync(x => x.Id == o.Id);
        Assert.Equal(PaymentOrderStatus.Paid, order.Status); Assert.Equal(Now, order.PaidAt);
        var target = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == o.Id);
        Assert.Equal(Now, target.StartsAt); Assert.Equal(Now.AddDays(30), target.EndsAt);
        Assert.Equal(o.PlanVersionId, target.PlanVersionId);
        var claims = await c.PaymentOrderCredits.AsNoTracking().Where(x => x.OrderId == o.Id).ToListAsync();
        Assert.NotEmpty(claims);
        foreach (var claim in claims)
        {
            Assert.Null(claim.ReleasedAt);
            var p = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.Id == claim.PeriodId);
            Assert.Equal(Now, p.TerminatedAt); Assert.Equal(o.Id, p.TerminatedByOrderId);
            Assert.Equal(claim.OriginalEndsAt, p.EndsAt);
        }
        var history = await c.PaymentOrderStatusHistories.AsNoTracking().SingleAsync(h => h.PaymentOrderId == o.Id && h.ToStatus == PaymentOrderStatus.Paid);
        Assert.Equal(Now, history.OccurredAt);
        Assert.Null(await new PaymentOrderRepository(c, Clock).GetBlockingSubscriptionAsync(o.UserId));
        Assert.Equal(LocalMateAI.Application.DTOs.Email.EmailTemplateNames.UpgradePaymentReceipt,
            (await c.EmailOutboxMessages.SingleAsync(e => e.DeduplicationKey == $"payment-receipt:{o.Id}")).TemplateName);
        Assert.Equal(1, await c.EmailOutboxMessages.CountAsync(e => e.DeduplicationKey == $"payment-receipt:{o.Id}"));
    }

    [Fact]
    public async Task FullTermAndFutureZeroCreditSources_PinnedVersion_MeAndAdminEffectiveEnd()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var trip = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        await SubscriptionUpgradeReservationPostgresTests.PaidPeriod(c, user, trip, Now);
        var legacy = new UserSubscription { UserId = user, PlanCode = PlanCode.TripPass, StartsAt = Now.AddDays(20), EndsAt = Now.AddDays(40) };
        c.Add(legacy); await c.SaveChangesAsync();
        c.Add(new SubscriptionPeriod { UserId = user, PlanId = trip.PlanId, PlanVersionId = trip.Id,
            LegacyUserSubscriptionId = legacy.Id, StartsAt = legacy.StartsAt, EndsAt = legacy.EndsAt });
        await c.SaveChangesAsync();
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user, PaymentOrderStatus.Pending);
        Assert.Contains(await c.PaymentOrderCredits.ToListAsync(), x => x.CalculatedCreditAmount == 0);
        await PlanVersionFoundationPostgresTests.PublishAsync(c, o.PlanId!.Value, 2, 99000, 45, null, null);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true))).Status);
        await AssertPaid(c, o);
        Assert.Equal(2, await c.SubscriptionPeriods.CountAsync(p => p.TerminatedAt < p.StartsAt));
        var effective = await EffectiveSubscriptionResolver.ResolveAsync(new SubscriptionRepository(c), user, Now);
        Assert.Equal("MEMBERSHIP", effective.Plan.Code); Assert.Equal(Now.AddDays(30), effective.EffectiveUntil);
        Assert.Equal(effective.EffectiveUntil, effective.PaidThrough);
        var me = await new SubscriptionService(new UserRepository(c), new SubscriptionRepository(c),
            new UsageEventRepository(c), new TripRepository(c), Clock,
            new LlmCallLogRepository(c), new FakeSystemSettingProvider()).GetMySubscriptionAsync(user);
        Assert.Equal("Membership", me!.Plan); Assert.Equal(Now.AddDays(30), me.EffectiveUntil);
        var admin = new AdminPlanRepository(c);
        Assert.Equal(0, (await admin.GetPlanAsync(trip.PlanId, Now))!.ActiveSubscriberCount);
        Assert.Equal(1, (await admin.GetPlanAsync(o.PlanId.Value, Now))!.ActiveSubscriberCount);
        var quote = await Checkout(c, new()).GetCheckoutQuoteAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.PlanAlreadyActive, quote.Status);
    }

    [Theory]
    [InlineData(PaymentOrderType.Purchase)]
    [InlineData(PaymentOrderType.Renewal)]
    public async Task OrdinaryTailIgnoresRawTerminatedEndAndStillQueuesReceipt(PaymentOrderType type)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user, PaymentOrderStatus.Pending);
        await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        var trip = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        var order = new PaymentOrder { UserId = user, PlanId = trip.PlanId, PlanVersionId = trip.Id,
            PlanVersionBinding = PlanVersionBinding.Native, Amount = trip.Price, Type = type, ExpiresAt = Now.AddMinutes(15) };
        await new PaymentOrderRepository(c, Clock).AddAsync(order);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true))).Status);
        var period = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == order.Id);
        Assert.Equal(Now, period.StartsAt); Assert.Equal(Now.AddDays(7), period.EndsAt);
        Assert.Equal(0, order.CreditAmount);
        Assert.True(await c.EmailOutboxMessages.AnyAsync(e => e.DeduplicationKey == $"payment-receipt:{order.Id}"));
    }

    [Fact]
    public async Task ChainedUpgradeUsesPurchasedGrossVersionAndNeverReusesConsumedLowerClaim()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var first = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user, PaymentOrderStatus.Pending);
        await Settlement(c).ApplyVerifiedPaymentAsync(new(first.ProviderOrderCode, first.Amount, true));
        var (top, _) = await PlanVersionFoundationPostgresTests.CustomAsync(c, "TOP_AFTER_UPGRADE", 300, 100000, 20, null, null);
        var second = await Checkout(c, new()).CheckoutAsync(user, top.Code);
        Assert.Equal(PaymentIntentResultStatus.Success, second.Status);
        Assert.Equal(57000, second.Response!.CreditAmount); Assert.Equal(43000, second.Response.Amount);
        var order = await c.PaymentOrders.SingleAsync(o => o.Id == second.Response.OrderId);
        var claim = await c.PaymentOrderCredits.AsNoTracking().SingleAsync(x => x.OrderId == order.Id);
        var membership = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == first.Id);
        Assert.Equal(membership.Id, claim.PeriodId); Assert.Equal(membership.EndsAt, claim.OriginalEndsAt);
        Assert.Equal(57033, claim.CalculatedCreditAmount);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true))).Status);
        var period = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == order.Id);
        Assert.Equal(Now.AddDays(20), period.EndsAt);
        Assert.Null((await c.PaymentOrderCredits.AsNoTracking().SingleAsync(x => x.OrderId == first.Id)).ReleasedAt);
        Assert.Null(await new PaymentOrderRepository(c, Clock).GetBlockingSubscriptionAsync(user));
    }

    [Fact]
    public async Task CustomInactivePurchasedVersionStillSettlesOriginalFullTerm()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var (plan, version) = await PlanVersionFoundationPostgresTests.CustomAsync(c, "CUSTOM_UPGRADE", 300, 45000, 10, 2, 4);
        var checkout = await Checkout(c, new()).CheckoutAsync(user, plan.Code);
        Assert.Equal(PaymentIntentResultStatus.Success, checkout.Status);
        var order = await c.PaymentOrders.SingleAsync(o => o.Id == checkout.Response!.OrderId);
        plan.IsActive = false; await c.SaveChangesAsync();
        await PlanVersionFoundationPostgresTests.PublishAsync(c, plan.Id, 2, 99000, 90, null, null);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true))).Status);
        var p = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SourcePaymentOrderId == order.Id);
        Assert.Equal(version.Id, p.PlanVersionId); Assert.Equal(Now, p.StartsAt); Assert.Equal(Now.AddDays(10), p.EndsAt);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Expired)]
    public async Task LatePaidWithUnreleasedClaimsStillGrantsFullTerm(PaymentOrderStatus status)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user, status);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true))).Status);
        await AssertPaid(c, o);
        Assert.Equal(UpgradeReleaseStatus.RequiresSettlement,
            (await SubscriptionUpgradeReservationPostgresTests.Service(c).ResolveForReplacementAsync(user, o.Id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnderpaidAndWrongProviderAmountKeepSourcesUnchanged(bool mismatch)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user, PaymentOrderStatus.Pending);
        var g = new Gateway { Amount = o.Amount - 1, ProviderStatus = mismatch ? PaymentGatewayOrderStatus.Paid : PaymentGatewayOrderStatus.Underpaid };
        var result = await new PaymentReconciliationService(new PaymentOrderRepository(c, Clock), g, Settlement(c), Clock,
            NullLogger<PaymentReconciliationService>.Instance).ReconcileAsync(o.Id, new(PaymentStatusChangeSource.AdminReconcile, user));
        Assert.Equal(mismatch ? PaymentSettlementStatus.AmountMismatch : PaymentSettlementStatus.NonSuccessful, result.SettlementStatus);
        Assert.Equal(PaymentOrderStatus.Failed, result.LocalStatusAfter);
        Assert.Null((await c.SubscriptionPeriods.AsNoTracking().SingleAsync()).TerminatedAt);
        Assert.Null((await c.PaymentOrderCredits.AsNoTracking().SingleAsync()).ReleasedAt);
        var history = await c.PaymentOrderStatusHistories.AsNoTracking().SingleAsync(h => h.PaymentOrderId == o.Id && h.ToStatus == PaymentOrderStatus.Failed);
        Assert.Equal(user, history.ActorUserId); Assert.Equal(PaymentStatusChangeSource.AdminReconcile, history.Source);
    }

    [Fact]
    public async Task PublicCheckoutSnapshot_NetworkOutsideLocks_ExistingPendingNeverProbed()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var g = new Gateway { BeforeCreate = async r =>
        {
            Assert.Null(c.Database.CurrentTransaction);
            await using var probe = db.Context(); await using var tx = await probe.Database.BeginTransactionAsync();
            await probe.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={user} FOR UPDATE NOWAIT""").SingleAsync();
            await probe.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "PaymentOrders" WHERE "Id"={r.OrderId} FOR UPDATE NOWAIT""").SingleAsync();
            Assert.Single(await probe.PaymentOrderCredits.Where(x => x.OrderId == r.OrderId).ToListAsync());
        } };
        var service = Checkout(c, g);
        var result = await service.CheckoutAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        var o = await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Type == PaymentOrderType.Upgrade);
        Assert.Equal("Upgrade", result.Response!.Type); Assert.Equal(59000, result.Response.ListPrice);
        Assert.Equal(10000, result.Response.CreditAmount); Assert.Equal(49000, result.Response.Amount);
        Assert.Equal(o.Amount, Assert.Single(g.Requests).Amount);
        var again = await service.CheckoutAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.PendingOrderExists, again.Status);
        Assert.Equal(o.Id, again.Response!.OrderId); Assert.Single(g.Requests); Assert.Equal(0, g.Lookups);
    }

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Cancelled)]
    [InlineData(PaymentGatewayOrderStatus.Paid)]
    [InlineData(PaymentGatewayOrderStatus.Underpaid)]
    [InlineData(PaymentGatewayOrderStatus.Pending)]
    [InlineData(PaymentGatewayOrderStatus.Processing)]
    [InlineData(PaymentGatewayOrderStatus.Unknown)]
    public async Task FailedLinkRetry_ReleasesOnlyProvenNoFunds_RecomputesOrSettlesWithoutSecondLookup(PaymentGatewayOrderStatus provider)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var g = new Gateway { Unavailable = true };
        var service = Checkout(c, g);
        Assert.Equal(PaymentIntentResultStatus.GatewayUnavailable, (await service.CheckoutAsync(user, "Membership")).Status);
        var old = await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Type == PaymentOrderType.Upgrade);
        Assert.Equal(PaymentOrderStatus.Failed, old.Status);
        Assert.Null((await c.PaymentOrderCredits.AsNoTracking().SingleAsync()).ReleasedAt);
        Assert.Equal(PaymentIntentResultStatus.AnotherPendingOrder, (await service.CheckoutAsync(user, "Membership")).Status);
        g.Unavailable = false; g.ProviderStatus = provider; g.Amount = old.Amount;
        var lookups = g.Lookups;
        await PlanVersionFoundationPostgresTests.PublishAsync(c, old.PlanId!.Value, 2, 69000, 45, null, null);
        var result = await service.CheckoutAsync(user, "Membership");
        Assert.Equal(lookups + 1, g.Lookups);
        if (provider == PaymentGatewayOrderStatus.Cancelled)
        {
            Assert.Equal(PaymentIntentResultStatus.Success, result.Status); Assert.Equal(2, g.Requests.Count);
            Assert.Equal(69000, result.Response!.ListPrice); Assert.Equal(59000, result.Response.Amount);
            Assert.True((await c.PaymentOrderCredits.AsNoTracking().SingleAsync(x => x.OrderId == old.Id)).HasValidReleaseEvidence());
        }
        else if (provider == PaymentGatewayOrderStatus.Paid)
        {
            Assert.Equal(PaymentIntentResultStatus.PlanAlreadyActive, result.Status); Assert.Single(g.Requests);
            await AssertPaid(c, old);
        }
        else
        {
            Assert.Equal(PaymentIntentResultStatus.AnotherPendingOrder, result.Status); Assert.Single(g.Requests);
            Assert.Null((await c.PaymentOrderCredits.AsNoTracking().SingleAsync()).ReleasedAt);
            Assert.All(await c.SubscriptionPeriods.AsNoTracking().ToListAsync(), p => Assert.Null(p.TerminatedAt));
        }
    }

    [Fact]
    public async Task ReviewBlockerRefreshesOrderTrackedBeforeAnotherTransaction()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var old = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user);
        await using (var other = db.Context())
        {
            Assert.Equal(UpgradeReleaseStatus.Released, (await SubscriptionUpgradeReservationPostgresTests.Service(other,
                new() { Amount = old.Amount }).ResolveForReplacementAsync(user, old.Id)).Status);
            Assert.Equal(PaymentSettlementStatus.CreditConflict,
                (await Settlement(other).ApplyVerifiedPaymentAsync(new(old.ProviderOrderCode, old.Amount, true))).Status);
        }
        Assert.Equal(PaymentOrderStatus.Failed, old.Status);
        var g = new Gateway();
        Assert.Equal(PaymentIntentResultStatus.PaymentReviewRequired, (await Checkout(c, g).CheckoutAsync(user, "Membership")).Status);
        Assert.Equal(PaymentOrderStatus.ReviewRequired, old.Status);
        Assert.Empty(g.Requests); Assert.Equal(0, g.Lookups);
    }

    [Fact]
    public async Task LatePaymentAfterSafeRelease_ReviewBlocksSubscriptionButNotSingle_ReplacementUntouched()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var old = await SubscriptionUpgradeReservationPostgresTests.Prepared(c, user);
        Assert.Equal(UpgradeReleaseStatus.Released, (await SubscriptionUpgradeReservationPostgresTests.Service(c,
            new() { Amount = old.Amount }).ResolveForReplacementAsync(user, old.Id)).Status);
        var next = await SubscriptionUpgradeReservationPostgresTests.Service(c).PrepareAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, next.Status);
        Assert.Equal(PaymentSettlementStatus.CreditConflict, (await Settlement(c).ApplyVerifiedPaymentAsync(new(old.ProviderOrderCode, old.Amount, true))).Status);
        Assert.Equal(PaymentSettlementStatus.CreditConflict, (await Settlement(c).ApplyVerifiedPaymentAsync(new(old.ProviderOrderCode, old.Amount, true))).Status);
        var review = await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == old.Id);
        Assert.Equal(PaymentOrderStatus.ReviewRequired, review.Status); Assert.Null(review.PaidAt);
        Assert.Single(await c.PaymentOrderStatusHistories.Where(h => h.PaymentOrderId == old.Id && h.ToStatus == PaymentOrderStatus.ReviewRequired).ToListAsync());
        Assert.All(await c.SubscriptionPeriods.AsNoTracking().ToListAsync(), p => Assert.Null(p.TerminatedAt));
        Assert.Null((await c.PaymentOrderCredits.AsNoTracking().SingleAsync(x => x.OrderId == next.OrderId)).ReleasedAt);
        Assert.Equal(PaymentIntentResultStatus.PaymentReviewRequired, (await Checkout(c, new()).CheckoutAsync(user, "Membership")).Status);
        Assert.Equal(PaymentIntentResultStatus.PaymentReviewRequired, (await Checkout(c, new()).RenewAsync(user)).Status);
        var single = await SingleItineraryPostgresTests.Order(c, user);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c).ApplyVerifiedPaymentAsync(new(single.ProviderOrderCode, single.Amount, true))).Status);
        Assert.Single(await c.SingleItineraryEntitlements.Where(e => e.SourcePaymentOrderId == single.Id).ToListAsync());
    }

    [Theory]
    [InlineData("history")]
    [InlineData("termination")]
    [InlineData("period")]
    [InlineData("after_write")]
    public async Task ControlledFailureRollsBackAllSettlementMutations(string phase)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user, PaymentOrderStatus.Pending);
        var count = await seed.PaymentOrderStatusHistories.CountAsync();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Connection,
            x => x.UseNetTopologySuite()).AddInterceptors(new FailSave(phase)).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true)));
        Assert.Equal(PaymentOrderStatus.Pending, (await seed.PaymentOrders.AsNoTracking().SingleAsync(x => x.Id == o.Id)).Status);
        Assert.Null((await seed.SubscriptionPeriods.AsNoTracking().SingleAsync()).TerminatedAt);
        Assert.False(await seed.SubscriptionPeriods.AnyAsync(p => p.SourcePaymentOrderId == o.Id));
        Assert.Equal(count, await seed.PaymentOrderStatusHistories.CountAsync());
        Assert.False(await seed.EmailOutboxMessages.AnyAsync(e => e.DeduplicationKey == $"payment-receipt:{o.Id}"));
    }

    private sealed class FailSave(string phase) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var c = data.Context!;
            var hit = phase == "history" ? c.ChangeTracker.Entries<PaymentOrderStatusHistory>().Any(e => e.State == EntityState.Added)
                : c.ChangeTracker.Entries<SubscriptionPeriod>().Any(e => e.State == (phase == "termination" ? EntityState.Modified : EntityState.Added));
            if (hit && phase != "after_write") throw new InvalidOperationException("controlled settlement rollback");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (phase == "after_write") throw new InvalidOperationException("controlled rollback after SQL write");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task LateGiSTConflictRollsBackGrantAndPersistsOnlyReview()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user, PaymentOrderStatus.Pending);
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Connection,
            x => x.UseNetTopologySuite()).AddInterceptors(new InjectTargetOverlap()).Options);
        Assert.Equal(PaymentSettlementStatus.CreditConflict,
            (await Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true))).Status);
        Assert.Null((await seed.SubscriptionPeriods.AsNoTracking().SingleAsync()).TerminatedAt);
        Assert.Equal(PaymentOrderStatus.ReviewRequired, (await seed.PaymentOrders.AsNoTracking().SingleAsync(x => x.Id == o.Id)).Status);
        Assert.False(await seed.SubscriptionPeriods.AnyAsync(p => p.SourcePaymentOrderId == o.Id));
        Assert.Empty(await seed.UserSubscriptions.ToListAsync());
        Assert.Single(await seed.PaymentOrderStatusHistories.Where(h => h.PaymentOrderId == o.Id && h.ToStatus == PaymentOrderStatus.ReviewRequired).ToListAsync());
        Assert.False(await seed.EmailOutboxMessages.AnyAsync(e => e.DeduplicationKey == $"payment-receipt:{o.Id}"));
    }

    private sealed class InjectTargetOverlap : SaveChangesInterceptor
    {
        private bool injected;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var c = (AppDbContext)data.Context!;
            var p = c.ChangeTracker.Entries<SubscriptionPeriod>().SingleOrDefault(e => e.State == EntityState.Added)?.Entity;
            if (injected || p is null) return result;
            injected = true;
            // Disposable-test fault injection after overlap validation, without disabling any guard.
            var legacyId = Guid.NewGuid(); var periodId = Guid.NewGuid();
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "UserSubscriptions" ("Id","UserId","PlanCode","StartsAt","EndsAt","CreatedAt","UpdatedAt")
                VALUES ({legacyId},{p.UserId},'Membership',{p.StartsAt},{p.EndsAt},{Now},{Now});
                INSERT INTO "SubscriptionPeriods" ("Id","UserId","PlanId","PlanVersionId","StartsAt","EndsAt","LegacyUserSubscriptionId","CreatedAt","UpdatedAt")
                VALUES ({periodId},{p.UserId},{p.PlanId},{p.PlanVersionId},{p.StartsAt},{p.EndsAt},{legacyId},{Now},{Now});
                """, ct);
            return result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C1_C2_DuplicateWebhookOrWebhookVsReconciliation_OneGrant(bool reconcile)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user, PaymentOrderStatus.Pending);
        await using var a = db.Context(); await using var b = db.Context();
        var g = new Gateway { Notification = new(o.ProviderOrderCode, o.Amount, true), Amount = o.Amount, ProviderStatus = PaymentGatewayOrderStatus.Paid };
        var first = Webhook(a, g).ProcessAsync("fake verified upgrade A");
        Task second = reconcile ? new PaymentReconciliationService(new PaymentOrderRepository(b, Clock), g,
            Settlement(b), Clock, NullLogger<PaymentReconciliationService>.Instance).ReconcileAsync(o.Id, new(PaymentStatusChangeSource.AdminReconcile, user))
            : Webhook(b, g).ProcessAsync("fake verified upgrade B");
        await Task.WhenAll(first, second);
        await AssertPaid(seed, o);
        Assert.Equal(reconcile ? 1 : 2, await seed.PaymentWebhookReceipts.CountAsync(r => r.PaymentOrderId == o.Id));
    }

    [Fact]
    public async Task C3_SettlementVsRelease_NeverReleaseConsumedClaim()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user);
        await using var a = db.Context(); await using var b = db.Context();
        var settle = Settlement(a).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        var release = SubscriptionUpgradeReservationPostgresTests.Service(b, new() { Amount = o.Amount }).ResolveForReplacementAsync(user, o.Id);
        await Task.WhenAll(settle, release);
        var period = await seed.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.PlanId != o.PlanId);
        var claim = await seed.PaymentOrderCredits.AsNoTracking().SingleAsync();
        var result = await settle;
        if (result.Status == PaymentSettlementStatus.Settled) { Assert.Null(claim.ReleasedAt); Assert.Equal(o.Id, period.TerminatedByOrderId); await AssertPaid(seed, o); }
        else { Assert.Equal(PaymentSettlementStatus.CreditConflict, result.Status); Assert.NotNull(claim.ReleasedAt); Assert.Null(period.TerminatedAt); }
    }

    [Fact]
    public async Task C4_SettlementVsReplacement_NoDoubleCredit()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user);
        await using var a = db.Context(); await using var b = db.Context();
        var settle = Settlement(a).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        var replacement = Checkout(b, new() { Amount = o.Amount }).CheckoutAsync(user, "Membership");
        await Task.WhenAll(settle, replacement);
        var claims = await seed.PaymentOrderCredits.AsNoTracking().Where(c => c.ReleasedAt == null).ToListAsync();
        Assert.Single(claims);
        Assert.True(await seed.SubscriptionPeriods.CountAsync(p => p.PlanId == o.PlanId) <= 1);
        Assert.True(await seed.PaymentOrders.CountAsync(p => p.Status == PaymentOrderStatus.Pending && p.Type == PaymentOrderType.Upgrade) <= 1);
    }

    [Fact]
    public async Task C5_TargetInsertWinsUserLock_UpgradeFailsClosed()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user);
        await using var writer = db.Context(); await using var tx = await writer.Database.BeginTransactionAsync();
        await writer.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={user} FOR UPDATE""").SingleAsync();
        var legacy = new UserSubscription { UserId = user, PlanCode = PlanCode.Membership, StartsAt = Now, EndsAt = Now.AddDays(30) };
        writer.Add(legacy); await writer.SaveChangesAsync();
        writer.Add(new SubscriptionPeriod { UserId = user, PlanId = o.PlanId!.Value, PlanVersionId = o.PlanVersionId!.Value,
            LegacyUserSubscriptionId = legacy.Id, StartsAt = legacy.StartsAt, EndsAt = legacy.EndsAt });
        await writer.SaveChangesAsync();
        await using var c = db.Context();
        var settle = Settlement(c).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        await tx.CommitAsync();
        Assert.Equal(PaymentSettlementStatus.CreditConflict, (await settle).Status);
        Assert.Equal(PaymentOrderStatus.ReviewRequired, (await seed.PaymentOrders.AsNoTracking().SingleAsync(x => x.Id == o.Id)).Status);
        Assert.All(await seed.SubscriptionPeriods.AsNoTracking().ToListAsync(), p => Assert.Null(p.TerminatedAt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C6_LinkCompletionAfterSettlementCannotDowngradePaid(bool unavailable)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var c = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(c);
        var g = new Gateway { Unavailable = unavailable, BeforeCreate = async r =>
        {
            await using var other = db.Context();
            Assert.Equal(PaymentSettlementStatus.Settled,
                (await Settlement(other).ApplyVerifiedPaymentAsync(new(r.ProviderOrderCode, r.Amount, true))).Status);
        } };
        var result = await Checkout(c, g).CheckoutAsync(user, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, result.Status); Assert.Equal("Paid", result.Response!.Status);
        await AssertPaid(c, await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Type == PaymentOrderType.Upgrade));
    }

    [Fact]
    public async Task C7_TwoPublicCheckouts_OnePendingOrderAndOneNetworkCreate()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        await using var a = db.Context(); await using var b = db.Context();
        var g = new Gateway();
        var results = await Task.WhenAll(Checkout(a, g).CheckoutAsync(user, "Membership"), Checkout(b, g).CheckoutAsync(user, "Membership"));
        Assert.Contains(results, r => r.Status == PaymentIntentResultStatus.Success);
        Assert.Single(g.Requests);
        Assert.Single(await seed.PaymentOrders.Where(o => o.Type == PaymentOrderType.Upgrade).ToListAsync());
        Assert.Single(await seed.PaymentOrderCredits.ToListAsync());
    }

    [Fact]
    public async Task C8_UpgradeSettlementAndSinglePendingRemainIndependent()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await SubscriptionUpgradeReservationPostgresTests.Seed(seed);
        var o = await SubscriptionUpgradeReservationPostgresTests.Prepared(seed, user, PaymentOrderStatus.Pending);
        var single = await SingleItineraryPostgresTests.Order(seed, user);
        await using var a = db.Context(); await using var b = db.Context();
        await Task.WhenAll(Settlement(a).ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true)),
            Settlement(b).ApplyVerifiedPaymentAsync(new(single.ProviderOrderCode, single.Amount, true)));
        await AssertPaid(seed, o);
        Assert.Single(await seed.SingleItineraryEntitlements.Where(e => e.SourcePaymentOrderId == single.Id).ToListAsync());
        Assert.Equal(0, single.CreditAmount);
    }
}
