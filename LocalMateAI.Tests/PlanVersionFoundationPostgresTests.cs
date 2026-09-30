using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class PlanVersionFoundationPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FreshMigration_SeedsExactBuiltinTermsAndCleanModel()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var context = db.Context();
        var repo = new SubscriptionRepository(context);
        var plans = await repo.GetPlansAsync();
        Assert.Equal(new[] { "FREE", "TRIP_PASS", "MEMBERSHIP" }, plans.Select(p => p.Code));
        Assert.Equal(new[] { 0, 100, 200 }, plans.Select(p => p.EntitlementPriority));
        var versions = await context.SubscriptionPlanVersions.OrderBy(v => v.Price).ToListAsync();
        Assert.Equal(new[] { 0m, 19000m, 59000m }, versions.Select(v => v.Price));
        Assert.Equal(new int?[] { null, 7, 30 }, versions.Select(v => v.DurationDays));
        Assert.All(versions, v => { Assert.Null(v.PublishedAt); Assert.Equal(PlanVersionOrigin.LegacyBaseline, v.Origin); });
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.SubscriptionPeriods.ToListAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task CheckoutV1_PublishV2_ReuseV1_SettleV1_RenewV2_AppendAndResolveBoundary()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await UserAsync(c);
        var (plan, v1) = await CustomAsync(c, "EXPLORER", 300, 50000, 10, 2, 4);
        var clock = new Clock(Now);
        var gateway = new Gateway();
        var checkout = Payment(c, clock, gateway);
        var first = await checkout.CheckoutAsync(user.Id, plan.Code);
        Assert.Equal(PaymentIntentResultStatus.Success, first.Status);
        var order = await c.PaymentOrders.SingleAsync();
        Assert.Null(order.PlanCode);
        Assert.Equal(plan.Id, order.PlanId);
        Assert.Equal(v1.Id, order.PlanVersionId);
        Assert.Equal(50000, order.Amount);
        Assert.Equal(PlanVersionBinding.Native, order.PlanVersionBinding);

        var v2 = await PublishAsync(c, plan.Id, 2, 69000, 20, 1, 2);
        var reused = await checkout.CheckoutAsync(user.Id, plan.Code);
        Assert.Equal(PaymentIntentResultStatus.PendingOrderExists, reused.Status);
        Assert.Equal(1, gateway.Creates);
        Assert.Equal(v1.Id, (await c.PaymentOrders.AsNoTracking().SingleAsync()).PlanVersionId);
        Assert.Equal(50000, (await c.SubscriptionPlanVersions.AsNoTracking().SingleAsync(v => v.Id == v1.Id)).Price);

        await c.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPlans\" SET \"IsActive\"=false WHERE \"Id\"={plan.Id}");
        var settlement = Settlement(c, clock);
        var notification = new VerifiedPaymentNotification(order.ProviderOrderCode, order.Amount, true);
        Assert.Equal(PaymentSettlementStatus.Settled, (await settlement.ApplyVerifiedPaymentAsync(notification)).Status);
        Assert.Equal(PaymentSettlementStatus.AlreadyPaid, (await settlement.ApplyVerifiedPaymentAsync(notification)).Status);
        var p1 = await c.SubscriptionPeriods.AsNoTracking().SingleAsync();
        Assert.Equal(v1.Id, p1.PlanVersionId);
        Assert.Equal(Now.AddDays(10), p1.EndsAt);
        Assert.Equal(1, await c.EmailOutboxMessages.CountAsync());
        Assert.Contains("Explorer", (await c.EmailOutboxMessages.SingleAsync()).Subject);

        await c.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPlans\" SET \"IsActive\"=true WHERE \"Id\"={plan.Id}");
        var renewal = await checkout.RenewAsync(user.Id);
        Assert.Equal(PaymentIntentResultStatus.Success, renewal.Status);
        var renewalOrder = await c.PaymentOrders.SingleAsync(o => o.Type == PaymentOrderType.Renewal);
        Assert.Equal(v2.Id, renewalOrder.PlanVersionId);
        Assert.Equal(69000, renewalOrder.Amount);
        Assert.Equal(PaymentSettlementStatus.Settled, (await settlement.ApplyVerifiedPaymentAsync(
            new(renewalOrder.ProviderOrderCode, renewalOrder.Amount, true))).Status);
        var periods = await c.SubscriptionPeriods.AsNoTracking().OrderBy(p => p.StartsAt).ToListAsync();
        Assert.Equal(2, periods.Count);
        Assert.Equal(p1.EndsAt, periods[1].StartsAt);
        Assert.Equal(p1.EndsAt.AddDays(20), periods[1].EndsAt);
        var before = await EffectiveSubscriptionResolver.ResolveAsync(new SubscriptionRepository(c), user.Id, p1.EndsAt.AddTicks(-1));
        var after = await EffectiveSubscriptionResolver.ResolveAsync(new SubscriptionRepository(c), user.Id, p1.EndsAt);
        Assert.Equal(v1.Id, before.Version.Id);
        Assert.Equal(2, before.Version.GenerateLimit);
        Assert.Equal(4, before.Version.SavedTripLimit);
        Assert.Equal(v2.Id, after.Version.Id);
        Assert.Equal(1, after.Version.GenerateLimit);
        Assert.Equal(2, after.Version.SavedTripLimit);
        Assert.Equal(periods[1].EndsAt, before.PaidThrough);
        Assert.Equal(p1.EndsAt, before.EffectiveUntil);
        Assert.Empty(await c.UserSubscriptions.ToListAsync());
        Assert.Equal(2, await c.EmailOutboxMessages.CountAsync());

        clock.Now = periods[1].EndsAt.AddDays(1);
        var later = await checkout.CheckoutAsync(user.Id, plan.Code);
        Assert.Equal(PaymentIntentResultStatus.Success, later.Status);
        Assert.Equal(v2.Id, (await c.PaymentOrders.OrderByDescending(o => o.ProviderOrderCode).FirstAsync()).PlanVersionId);
    }

    [Fact]
    public async Task LegacyMigration_PreservesFinancialFactsAndOneWholeAggregate()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(previousSchema: true);
        await using var c = db.Context();
        var user = await UserAsync(c);
        var legacyId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var historicalId = Guid.NewGuid();
        var usageId = Guid.NewGuid();
        var trip = await TripAsync(c, user.Id);
        await c.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "UserSubscriptions" ("Id","UserId","PlanCode","StartsAt","EndsAt","CreatedAt","UpdatedAt")
            VALUES ({legacyId},{user.Id},'Membership',{Now},{Now.AddDays(60)},{Now},{Now});
            INSERT INTO "PaymentOrders" ("Id","UserId","PlanCode","Type","Amount","Status","ProviderOrderCode","ExpiresAt","PaidAt","CheckoutUrl","QrCode","CreatedAt","UpdatedAt")
            VALUES ({orderId},{user.Id},'Membership','Purchase',59000,'Paid',9901,{Now.AddMinutes(15)},{Now},'test-url','test-qr',{Now},{Now}),
            ({historicalId},{user.Id},'TripPass','Purchase',49000,'Pending',9902,{Now.AddMinutes(15)},NULL,'old-url','old-qr',{Now},{Now});
            INSERT INTO "UsageEvents" ("Id","UserId","Type","TripId","CreatedAt","UpdatedAt")
            VALUES ({usageId},{user.Id},'Generate',{trip.Id},{Now},{Now});
            """);
        await c.Database.MigrateAsync();
        var period = await c.SubscriptionPeriods.SingleAsync();
        Assert.Equal(legacyId, period.LegacyUserSubscriptionId);
        Assert.Null(period.SourcePaymentOrderId);
        Assert.Equal(Now, period.StartsAt);
        Assert.Equal(Now.AddDays(60), period.EndsAt);
        Assert.Equal(SubscriptionBaseline.VersionId(PlanCode.Membership), period.PlanVersionId);
        var orders = await c.PaymentOrders.AsNoTracking().OrderBy(o => o.ProviderOrderCode).ToListAsync();
        Assert.Equal(PaymentOrderStatus.Paid, orders[0].Status);
        Assert.Equal(59000, orders[0].Amount);
        Assert.Equal(Now, orders[0].PaidAt);
        Assert.Equal("test-url", orders[0].CheckoutUrl);
        Assert.Equal("test-qr", orders[0].QrCode);
        Assert.Equal(Now, orders[0].CreatedAt);
        Assert.Equal(Now.AddMinutes(15), orders[0].ExpiresAt);
        Assert.Equal(49000, orders[1].Amount);
        Assert.Equal(PaymentOrderStatus.Pending, orders[1].Status);
        Assert.All(orders, o => { Assert.Null(o.PlanVersionId); Assert.Equal(PlanVersionBinding.LegacyUnresolved, o.PlanVersionBinding); });
        Assert.Null((await c.UsageEvents.SingleAsync()).SubscriptionPeriodId);
        var result = await Settlement(c, new Clock(Now)).ApplyVerifiedPaymentAsync(new(9902, 49000, true));
        Assert.Equal(PaymentSettlementStatus.UnresolvedPlanVersion, result.Status);
        Assert.Equal(PaymentOrderStatus.Pending, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == historicalId)).Status);
        Assert.Equal(1, await c.SubscriptionPeriods.CountAsync());
        Assert.Empty(await c.EmailOutboxMessages.ToListAsync());
        Assert.Equal(1, await c.UserSubscriptions.CountAsync());
    }

    [Theory]
    [InlineData("duplicate_code")]
    [InlineData("invalid_code")]
    [InlineData("duplicate_priority")]
    [InlineData("negative_priority")]
    [InlineData("published_priority")]
    [InlineData("rename_code")]
    [InlineData("duplicate_version")]
    [InlineData("negative_price")]
    [InlineData("zero_duration")]
    [InlineData("negative_generate")]
    [InlineData("negative_saved")]
    [InlineData("free_deactivate")]
    [InlineData("free_delete")]
    [InlineData("free_pointer_clear")]
    [InlineData("version_update")]
    [InlineData("version_delete")]
    [InlineData("wrong_pointer_plan")]
    [InlineData("free_paid_terms")]
    [InlineData("paid_no_duration")]
    public async Task DatabaseGuards_RejectInvalidCatalogMutation(string mutation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var free = SubscriptionBaseline.PlanId(PlanCode.Free);
        var trip = SubscriptionBaseline.PlanId(PlanCode.TripPass);
        var freeVersion = SubscriptionBaseline.VersionId(PlanCode.Free);
        var tripVersion = SubscriptionBaseline.VersionId(PlanCode.TripPass);
        var id = Guid.NewGuid();
        var sql = mutation switch
        {
            "duplicate_code" => $"INSERT INTO \"SubscriptionPlans\" SELECT '{id}',\"Code\",'Other',false,true,NULL,900,now(),now() FROM \"SubscriptionPlans\" WHERE \"Id\"='{free}'",
            "invalid_code" => $"INSERT INTO \"SubscriptionPlans\" VALUES ('{id}','bad-code','Other',false,true,NULL,900,now(),now())",
            "duplicate_priority" => $"INSERT INTO \"SubscriptionPlans\" VALUES ('{id}','OTHER','Other',false,true,NULL,100,now(),now())",
            "negative_priority" => $"INSERT INTO \"SubscriptionPlans\" VALUES ('{id}','OTHER','Other',false,true,NULL,-1,now(),now())",
            "published_priority" => $"UPDATE \"SubscriptionPlans\" SET \"EntitlementPriority\"=999 WHERE \"Id\"='{trip}'",
            "rename_code" => $"UPDATE \"SubscriptionPlans\" SET \"Code\"='OTHER' WHERE \"Id\"='{trip}'",
            "free_deactivate" => $"UPDATE \"SubscriptionPlans\" SET \"IsActive\"=false WHERE \"Id\"='{free}'",
            "free_delete" => $"DELETE FROM \"SubscriptionPlans\" WHERE \"Id\"='{free}'",
            "free_pointer_clear" => $"UPDATE \"SubscriptionPlans\" SET \"CurrentVersionId\"=NULL WHERE \"Id\"='{free}'",
            "version_update" => $"UPDATE \"SubscriptionPlanVersions\" SET \"Price\"=123 WHERE \"Id\"='{tripVersion}'",
            "version_delete" => $"DELETE FROM \"SubscriptionPlanVersions\" WHERE \"Id\"='{tripVersion}'",
            "wrong_pointer_plan" => $"UPDATE \"SubscriptionPlans\" SET \"CurrentVersionId\"='{tripVersion}' WHERE \"Id\"='{free}'",
            _ => $"INSERT INTO \"SubscriptionPlanVersions\" (\"Id\",\"PlanId\",\"VersionNumber\",\"Price\",\"DurationDays\",\"GenerateLimit\",\"SavedTripLimit\",\"Origin\",\"PublishedAt\",\"CreatedAt\",\"UpdatedAt\") VALUES ('{id}','{(mutation == "free_paid_terms" ? free : trip)}',{(mutation == "duplicate_version" ? 1 : 2)},{(mutation == "negative_price" ? -1 : 19000)},{(mutation == "paid_no_duration" ? "NULL" : mutation == "zero_duration" ? "0" : "7")},{(mutation == "negative_generate" ? "-1" : "NULL")},{(mutation == "negative_saved" ? "-1" : "3")},'Published',now(),now(),now())"
        };
        var exception = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(exception.SqlState, new[] { "23514", "23505", "23503" });
        Assert.Equal(3, await c.SubscriptionPlans.CountAsync());
        Assert.Equal(3, await c.SubscriptionPlanVersions.CountAsync());
        Assert.Equal(freeVersion, (await c.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Id == free)).CurrentVersionId);
    }

    [Theory]
    [InlineData("period_update")]
    [InlineData("period_delete")]
    [InlineData("overlap")]
    [InlineData("duplicate_order_source")]
    [InlineData("wrong_period_plan")]
    [InlineData("wrong_period_owner")]
    [InlineData("wrong_duration")]
    [InlineData("order_amount")]
    [InlineData("order_version")]
    [InlineData("usage_owner")]
    [InlineData("paid_without_period")]
    [InlineData("period_without_paid")]
    public async Task DatabaseGuards_RejectCorruptPurchasedPeriodAndUsage(string mutation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await UserAsync(c);
        var other = await UserAsync(c);
        var (plan, version) = await CustomAsync(c, "EXPLORER", 300, 50000, 10, 2, 4);
        var order = await BoundOrderAsync(c, user.Id, plan.Id, version);
        Assert.Equal(PaymentSettlementStatus.Settled, (await Settlement(c, new Clock(Now))
            .ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true))).Status);
        var p = await c.SubscriptionPeriods.AsNoTracking().SingleAsync();
        var id = Guid.NewGuid();
        var second = await BoundOrderAsync(c, user.Id, plan.Id, version);
        var usageTrip = await TripAsync(c, other.Id);
        var sql = mutation switch
        {
            "period_update" => $"UPDATE \"SubscriptionPeriods\" SET \"EndsAt\"=\"EndsAt\"+interval '1 day' WHERE \"Id\"='{p.Id}'",
            "period_delete" => $"DELETE FROM \"SubscriptionPeriods\" WHERE \"Id\"='{p.Id}'",
            "order_amount" => $"UPDATE \"PaymentOrders\" SET \"Amount\"=1 WHERE \"Id\"='{order.Id}'",
            "order_version" => $"UPDATE \"PaymentOrders\" SET \"PlanVersionId\"='{SubscriptionBaseline.VersionId(PlanCode.Membership)}' WHERE \"Id\"='{order.Id}'",
            "paid_without_period" => $"UPDATE \"PaymentOrders\" SET \"Status\"='Paid',\"PaidAt\"=now() WHERE \"Id\"='{second.Id}'",
            "period_without_paid" => $"INSERT INTO \"SubscriptionPeriods\" (\"Id\",\"UserId\",\"PlanId\",\"PlanVersionId\",\"StartsAt\",\"EndsAt\",\"SourcePaymentOrderId\",\"CreatedAt\",\"UpdatedAt\") VALUES ('{id}','{user.Id}','{plan.Id}','{version.Id}','{Now.AddDays(10):O}','{Now.AddDays(20):O}','{second.Id}',now(),now())",
            "usage_owner" => $"INSERT INTO \"UsageEvents\" (\"Id\",\"UserId\",\"Type\",\"TripId\",\"SubscriptionPeriodId\",\"CreatedAt\",\"UpdatedAt\") VALUES ('{id}','{other.Id}','Generate','{usageTrip.Id}','{p.Id}',now(),now())",
            _ => $"INSERT INTO \"SubscriptionPeriods\" (\"Id\",\"UserId\",\"PlanId\",\"PlanVersionId\",\"StartsAt\",\"EndsAt\",\"SourcePaymentOrderId\",\"CreatedAt\",\"UpdatedAt\") VALUES ('{id}','{(mutation == "wrong_period_owner" ? other.Id : user.Id)}','{(mutation == "wrong_period_plan" ? SubscriptionBaseline.PlanId(PlanCode.Membership) : plan.Id)}','{version.Id}','{Now:O}','{Now.AddDays(mutation == "wrong_duration" ? 11 : 10):O}','{(mutation == "duplicate_order_source" ? order.Id : second.Id)}',now(),now())"
        };
        var exception = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(exception.SqlState, new[] { "23514", "23505", "23503", "23P01" });
        Assert.Equal(1, await c.SubscriptionPeriods.CountAsync());
        Assert.Equal(50000, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Amount);
    }

    [Fact]
    public async Task FreeCurrentVersion_IsImmediate_NoPeriodAndVietnamBucketExcludesPaidUsage()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await UserAsync(c);
        var repo = new SubscriptionRepository(c);
        await PublishAsync(c, SubscriptionBaseline.PlanId(PlanCode.Free), 2, 0, null, 2, 1);
        var free = await EffectiveSubscriptionResolver.ResolveAsync(repo, user.Id, Now);
        Assert.Equal(2, free.Version.GenerateLimit);
        Assert.Null(free.Period);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
        var month = VietnamMonthWindow.For(Now);
        var usage = new UsageEventRepository(c);
        var trip = await TripAsync(c, user.Id);
        await usage.AddAsync(new UsageEvent { UserId = user.Id, TripId = trip.Id, Type = UsageEventType.Generate, CreatedAt = month.StartUtc.AddTicks(-1), UpdatedAt = Now });
        await usage.AddAsync(new UsageEvent { UserId = user.Id, TripId = trip.Id, Type = UsageEventType.Generate, CreatedAt = month.StartUtc, UpdatedAt = Now });
        await usage.AddAsync(new UsageEvent { UserId = user.Id, TripId = trip.Id, Type = UsageEventType.Generate, CreatedAt = month.NextStartUtc, UpdatedAt = Now });
        Assert.Equal(1, await EffectiveSubscriptionResolver.CountGenerateAsync(usage, user.Id, free, Now));
    }

    [Fact]
    public async Task EfGuards_RejectModifiedVersionAndPeriodBeforeSave()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var version = await c.SubscriptionPlanVersions.FirstAsync();
        c.Entry(version).Property(v => v.Price).CurrentValue = 123m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
        c.ChangeTracker.Clear();
        var user = await UserAsync(c);
        var (plan, v) = await CustomAsync(c, "EXPLORER", 300, 50000, 10, 2, 4);
        var order = await BoundOrderAsync(c, user.Id, plan.Id, v);
        await Settlement(c, new Clock(Now)).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true));
        var period = await c.SubscriptionPeriods.SingleAsync();
        c.Entry(period).Property(p => p.EndsAt).CurrentValue = Now.AddDays(100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }

    internal static async Task<User> UserAsync(AppDbContext c)
    {
        var user = new User { FullName = "Version test", Email = $"version-{Guid.NewGuid():N}@localmate.test" };
        c.Users.Add(user);
        await c.SaveChangesAsync();
        return user;
    }
    internal static async Task<Trip> TripAsync(AppDbContext c, Guid userId)
    {
        var trip = new Trip
        {
            UserId = userId,
            Status = TripStatus.Draft,
            DurationHours = 3,
            StartLatitude = 10.77,
            StartLongitude = 106.69,
            BudgetMax = 300000
        };
        c.Trips.Add(trip);
        await c.SaveChangesAsync();
        return trip;
    }
    internal static async Task<(SubscriptionPlan, SubscriptionPlanVersion)> CustomAsync(AppDbContext c,
        string code, int priority, decimal price, int days, int? generate, int? saved)
    {
        var plan = new SubscriptionPlan { Code = code, Name = "Explorer", IsActive = true, EntitlementPriority = priority };
        c.SubscriptionPlans.Add(plan);
        await c.SaveChangesAsync();
        var version = await PublishAsync(c, plan.Id, 1, price, days, generate, saved);
        return (plan, version);
    }
    internal static async Task<SubscriptionPlanVersion> PublishAsync(AppDbContext c, Guid planId,
        int number, decimal price, int? days, int? generate, int? saved)
    {
        var version = new SubscriptionPlanVersion
        {
            PlanId = planId,
            VersionNumber = number,
            Price = price,
            DurationDays = days,
            GenerateLimit = generate,
            SavedTripLimit = saved,
            Origin = PlanVersionOrigin.Published,
            PublishedAt = Now
        };
        await new SubscriptionRepository(c).PublishVersionAsync(version);
        return version;
    }
    internal static async Task<PaymentOrder> BoundOrderAsync(AppDbContext c, Guid userId, Guid planId, SubscriptionPlanVersion version)
    {
        var order = new PaymentOrder
        {
            UserId = userId,
            PlanId = planId,
            PlanVersionId = version.Id,
            PlanVersionBinding = PlanVersionBinding.Native,
            Amount = version.Price,
            Status = PaymentOrderStatus.Pending,
            ExpiresAt = Now.AddMinutes(15),
            Type = PaymentOrderType.Purchase
        };
        await new PaymentOrderRepository(c).AddAsync(order);
        return order;
    }
    internal static PaymentSettlementService Settlement(AppDbContext c, TimeProvider clock) => new(
        new PaymentSettlementExecutor(c), new SubscriptionRepository(c), new UserRepository(c),
        new EmailOutboxRepository(c), clock, NullLogger<PaymentSettlementService>.Instance);
    private static PaymentService Payment(AppDbContext c, TimeProvider clock, Gateway gateway) => new(
        new UserRepository(c), new SubscriptionRepository(c), new PaymentOrderRepository(c),
        new PaymentOperationExecutor(c), gateway, Settlement(c, clock), clock, NullLogger<PaymentService>.Instance);
    internal sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class Gateway : IPaymentGateway
    {
        public int Creates { get; private set; }
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest request, CancellationToken ct = default)
        { Creates++; return Task.FromResult(PaymentLinkResult.Succeeded("https://example.invalid/test", "test-only")); }
        public Task<PaymentGatewayOrderResult> GetPaymentAsync(long code, CancellationToken ct = default) =>
            Task.FromResult(PaymentGatewayOrderResult.Unavailable(code));
        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string raw, CancellationToken ct = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Invalid());
    }
}
