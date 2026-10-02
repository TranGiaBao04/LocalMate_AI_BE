using LocalMateAI.Application.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeFoundationPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc);
    private const string Previous = "20261002035306_AddSystemSettings";
    private const string Migration = "20261002044046_AddSubscriptionUpgradeFoundation";

    private static async Task<(PaymentOrder Order, SubscriptionPeriod Period)> Source(AppDbContext c, int future = 0)
    {
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var version = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass));
        var order = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, version.PlanId, version);
        var result = await PlanVersionFoundationPostgresTests.Settlement(c,
            new PlanVersionFoundationPostgresTests.Clock(Now.AddDays(future)))
            .ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true));
        Assert.Equal(PaymentSettlementStatus.Settled, result.Status);
        return (order, await c.SubscriptionPeriods.SingleAsync(p => p.SourcePaymentOrderId == order.Id));
    }

    private static async Task<PaymentOrder> Upgrade(AppDbContext c, Guid userId, decimal credit = 9000)
    {
        var order = new PaymentOrder
        {
            UserId = userId, PlanId = SubscriptionBaseline.PlanId(PlanCode.TripPass),
            PlanVersionId = SubscriptionBaseline.VersionId(PlanCode.TripPass),
            PlanVersionBinding = PlanVersionBinding.Native, Type = PaymentOrderType.Upgrade,
            Amount = 19000 - credit, CreditAmount = credit, ExpiresAt = Now.AddMinutes(15)
        };
        c.PaymentOrders.Add(order);
        await c.SaveChangesAsync();
        return order;
    }

    private static async Task Claim(AppDbContext c, PaymentOrder order, SubscriptionPeriod period, decimal value = 9000)
    {
        c.PaymentOrderCredits.Add(new PaymentOrderCredit { OrderId = order.Id, PeriodId = period.Id,
            UserId = order.UserId, OriginalEndsAt = period.EndsAt, RemainingDays = 4, CalculatedCreditAmount = value });
        await c.SaveChangesAsync();
    }

    private static Task<int> InsertCredit(AppDbContext c, Guid order, Guid period, Guid user,
        DateTime ends, int days = 4, decimal value = 9000) => c.Database.ExecuteSqlAsync($"""
        INSERT INTO "PaymentOrderCredits"
        ("OrderId","PeriodId","UserId","OriginalEndsAt","RemainingDays","CalculatedCreditAmount")
        VALUES ({order},{period},{user},{ends},{days},{value})
        """);

    [Theory]
    [InlineData("negative")]
    [InlineData("purchase")]
    [InlineData("renewal")]
    [InlineData("single")]
    [InlineData("conservation")]
    [InlineData("financial_update")]
    public async Task InvalidFinancialSnapshotsRejected(string kind)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (source, _) = await Source(c);
        FormattableString sql = kind switch
        {
            "negative" => $"""UPDATE "PaymentOrders" SET "CreditAmount"=-1 WHERE "Id"={source.Id}""",
            "financial_update" => $"""UPDATE "PaymentOrders" SET "CreditAmount"=1 WHERE "Id"={source.Id}""",
            _ => $"""
                INSERT INTO "PaymentOrders"
                ("Id","UserId","ProductKind","PlanId","PlanVersionId","PlanVersionBinding","Amount","CreditAmount",
                 "Type","Status","ExpiresAt","CreatedAt","UpdatedAt","ProviderOrderCode")
                VALUES ({Guid.NewGuid()},{source.UserId},{"SubscriptionPlan"},{source.PlanId},{source.PlanVersionId},
                 {"Native"},{18000m},{(kind == "conservation" ? 500m : 1000m)},
                 {(kind == "renewal" ? "Renewal" : kind == "conservation" ? "Upgrade" : "Purchase")},
                 {"Pending"},{Now.AddMinutes(15)},{Now},{Now},{0L})
                """
        };
        if (kind == "single")
        {
            var single = await SingleItineraryPostgresTests.Order(c, source.UserId);
            sql = $"""UPDATE "PaymentOrders" SET "CreditAmount"=1 WHERE "Id"={single.Id}""";
        }
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync(sql));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9000)]
    public async Task UpgradeConservationAndSettlementFailClosed(int credit)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (source, _) = await Source(c);
        var order = await Upgrade(c, source.UserId, credit);
        Assert.Equal(19000, order.Amount + order.CreditAmount);
        var periods = await c.SubscriptionPeriods.CountAsync();
        var history = await c.PaymentOrderStatusHistories.CountAsync();
        var outbox = await c.EmailOutboxMessages.CountAsync();
        var result = await PlanVersionFoundationPostgresTests.Settlement(c,
            new PlanVersionFoundationPostgresTests.Clock(Now)).ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true));
        Assert.Equal(PaymentSettlementStatus.InvalidPaidPlan, result.Status);
        Assert.Equal(PaymentOrderStatus.Pending, (await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
        Assert.Equal(periods, await c.SubscriptionPeriods.CountAsync());
        Assert.Equal(history, await c.PaymentOrderStatusHistories.CountAsync());
        Assert.Equal(outbox, await c.EmailOutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("purchase")]
    [InlineData("renewal")]
    [InlineData("single")]
    [InlineData("ends")]
    [InlineData("days")]
    [InlineData("negative")]
    public async Task InvalidClaimRejected(string kind)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (source, p) = await Source(c);
        var user = kind == "owner" ? (await PlanVersionFoundationPostgresTests.UserAsync(c)).Id : source.UserId;
        var o = kind is "purchase" or "renewal" ? source
            : kind == "single" ? await SingleItineraryPostgresTests.Order(c, user) : await Upgrade(c, user);
        // A Renewal is valid commercially but is never a credit-reserving order.
        if (kind == "renewal")
        {
            var v = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == source.PlanVersionId);
            o = new PaymentOrder { UserId = user, PlanId = v.PlanId, PlanVersionId = v.Id,
                PlanVersionBinding = PlanVersionBinding.Native, Type = PaymentOrderType.Renewal,
                Amount = v.Price, ExpiresAt = Now.AddMinutes(15) };
            c.Add(o); await c.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<PostgresException>(() => InsertCredit(c, o.Id, p.Id, user,
            kind == "ends" ? p.EndsAt.AddDays(1) : p.EndsAt, kind == "days" ? 8 : 4, kind == "negative" ? -1 : 9000));
    }

    [Fact]
    public async Task ZeroCreditKept_DuplicateClaimRejected_ReleaseAllowsNewClaim()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (source, p) = await Source(c);
        var a = await Upgrade(c, source.UserId, 0);
        var b = await Upgrade(c, source.UserId, 0);
        await Claim(c, a, p, 0);
        await Assert.ThrowsAsync<PostgresException>(() => InsertCredit(c, b.Id, p.Id, source.UserId, p.EndsAt, value: 0));
        var row = await c.PaymentOrderCredits.SingleAsync();
        row.Release(Now);
        await c.SaveChangesAsync();
        await InsertCredit(c, b.Id, p.Id, source.UserId, p.EndsAt, value: 0);
        Assert.Equal(2, await c.PaymentOrderCredits.CountAsync());
        Assert.Equal(1, await c.PaymentOrderCredits.CountAsync(x => x.ReleasedAt == null));
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("reopen")]
    [InlineData("retime")]
    [InlineData("delete")]
    [InlineData("truncate")]
    public async Task CreditEvidenceImmutable(string kind)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (source, p) = await Source(c);
        var order = await Upgrade(c, source.UserId);
        await Claim(c, order, p);
        if (kind is "reopen" or "retime")
            await c.Database.ExecuteSqlAsync($"""UPDATE "PaymentOrderCredits" SET "ReleasedAt"={Now} WHERE "OrderId"={order.Id}""");
        if (kind == "truncate")
        {
            await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync("""TRUNCATE "PaymentOrderCredits" """));
            return;
        }
        FormattableString sql = kind switch
        {
            "delete" => $"""DELETE FROM "PaymentOrderCredits" WHERE "OrderId"={order.Id}""",
            "snapshot" => $"""UPDATE "PaymentOrderCredits" SET "RemainingDays"=1 WHERE "OrderId"={order.Id}""",
            "reopen" => $"""UPDATE "PaymentOrderCredits" SET "ReleasedAt"=NULL WHERE "OrderId"={order.Id}""",
            _ => $"""UPDATE "PaymentOrderCredits" SET "ReleasedAt"={Now.AddMinutes(1)} WHERE "OrderId"={order.Id}"""
        };
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync(sql));
    }

    [Theory]
    [InlineData("pair")]
    [InlineData("owner")]
    [InlineData("purchase")]
    [InlineData("unpaid")]
    [InlineData("ends")]
    [InlineData("delete")]
    [InlineData("truncate")]
    public async Task InvalidTerminationRejected(string kind)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (source, p) = await Source(c);
        var owner = kind == "owner" ? (await PlanVersionFoundationPostgresTests.UserAsync(c)).Id : source.UserId;
        var order = kind == "purchase" ? source : await Upgrade(c, owner);
        if (kind == "truncate")
        {
            await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync("""TRUNCATE "SubscriptionPeriods" CASCADE"""));
            return;
        }
        FormattableString sql = kind switch
        {
            "delete" => $"""DELETE FROM "SubscriptionPeriods" WHERE "Id"={p.Id}""",
            "ends" => $"""UPDATE "SubscriptionPeriods" SET "EndsAt"={p.EndsAt.AddDays(1)} WHERE "Id"={p.Id}""",
            "pair" => $"""UPDATE "SubscriptionPeriods" SET "TerminatedAt"={Now} WHERE "Id"={p.Id}""",
            _ => $"""UPDATE "SubscriptionPeriods" SET "TerminatedAt"={Now},"TerminatedByOrderId"={order.Id} WHERE "Id"={p.Id}"""
        };
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync(sql));
    }

    private static async Task<PaymentOrder> TerminateAndGrant(AppDbContext c, SubscriptionPeriod p)
    {
        var order = await Upgrade(c, p.UserId);
        await Claim(c, order, p);
        await using var tx = await c.Database.BeginTransactionAsync();
        // Deliberately write termination before Paid: the commit-time guard must tolerate either EF write order.
        p.Terminate(Now, order.Id);
        await c.SaveChangesAsync();
        c.SubscriptionPeriods.Add(new SubscriptionPeriod { UserId = p.UserId, PlanId = p.PlanId,
            PlanVersionId = order.PlanVersionId!.Value, SourcePaymentOrderId = order.Id,
            StartsAt = Now, EndsAt = Now.AddDays(7) });
        await c.SaveChangesAsync();
        order.Status = PaymentOrderStatus.Paid; order.PaidAt = Now;
        await c.SaveChangesAsync();
        await tx.CommitAsync();
        return order;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task EffectiveOverlapAndEmptyFuture_DeferredPaidGuard(int future)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (_, p) = await Source(c, future);
        await c.Entry(p).ReloadAsync();
        var original = (p.StartsAt, p.EndsAt, p.PlanVersionId, p.SourcePaymentOrderId, p.UpdatedAt);
        var order = await TerminateAndGrant(c, p);
        c.ChangeTracker.Clear();
        var stored = await c.SubscriptionPeriods.SingleAsync(x => x.Id == p.Id);
        Assert.Equal(original, (stored.StartsAt, stored.EndsAt, stored.PlanVersionId, stored.SourcePaymentOrderId, stored.UpdatedAt));
        Assert.Equal(order.Id, stored.TerminatedByOrderId);
        Assert.Equal(2, await c.SubscriptionPeriods.CountAsync());
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync(
            $"""UPDATE "SubscriptionPeriods" SET "TerminatedAt"=NULL,"TerminatedByOrderId"=NULL WHERE "Id"={p.Id}"""));
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync(
            $"""UPDATE "SubscriptionPeriods" SET "TerminatedAt"={Now.AddMinutes(1)} WHERE "Id"={p.Id}"""));
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync(
            $"""UPDATE "PaymentOrderCredits" SET "ReleasedAt"={Now} WHERE "OrderId"={order.Id}"""));
    }

    [Fact]
    public async Task TrueEffectiveOverlapRejected()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (_, p) = await Source(c);
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync($"""
            INSERT INTO "SubscriptionPeriods"
            ("Id","UserId","PlanId","PlanVersionId","StartsAt","EndsAt","CreatedAt","UpdatedAt")
            VALUES ({Guid.NewGuid()},{p.UserId},{p.PlanId},{p.PlanVersionId},{p.StartsAt},{p.EndsAt},{Now},{Now})
            """));
    }

    [Theory]
    [InlineData("Purchase")]
    [InlineData("Renewal")]
    [InlineData("Upgrade")]
    public async Task SingleStillPurchaseOnly(string type)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var u = await PlanVersionFoundationPostgresTests.UserAsync(c);
        await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlAsync($"""
            INSERT INTO "PaymentOrders" ("Id","UserId","ProductKind","SingleItineraryProductVersionId",
            "CheckoutAttemptId","PlanVersionBinding","Amount","CreditAmount","Type","Status","ExpiresAt","ProviderOrderCode","CreatedAt","UpdatedAt")
            VALUES ({Guid.NewGuid()},{u.Id},{"SingleItinerary"},{SingleItineraryBaseline.VersionId},
            {Guid.NewGuid()},NULL,{29000m},{(type == "Purchase" ? 1m : 0m)},{type},{"Pending"},{Now},{0L},{Now},{Now})
            """));
    }

    [Fact]
    public async Task FreshChainIndexesAndDefaults_ModelMatches()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        Assert.Equal(Migration, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(c.Database.HasPendingModelChanges());
        var (order, p) = await Source(c);
        Assert.Equal(0, (await c.PaymentOrders.AsNoTracking().SingleAsync(x => x.Id == order.Id)).CreditAmount);
        Assert.Null(p.TerminatedAt);
        Assert.Empty(await c.PaymentOrderCredits.ToListAsync());
        Assert.Equal(1, await c.SingleItineraryProductVersions.CountAsync());
        var indexes = await c.Database.SqlQueryRaw<string>("""SELECT indexname AS "Value" FROM pg_indexes WHERE schemaname='public'""").ToListAsync();
        Assert.Contains("UX_OrderCredits_UnreleasedPeriod", indexes);
        Assert.Contains("IX_PaymentOrders_PaidAt_Paid", indexes);
        Assert.Contains("IX_Users_CreatedAt", indexes);
        Assert.Contains("IX_Trips_CreatedAt", indexes);
        Assert.Contains("IX_SystemSettings_UpdatedByUserId", indexes);
        Assert.Empty(await c.SystemSettings.ToListAsync());
    }

    [Fact]
    public async Task UpgradeFromSystemSettings_PreservesNativeLegacySingleAndSettingsEvidence()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: Previous);
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var plan = SubscriptionBaseline.PlanId(PlanCode.TripPass);
        var version = SubscriptionBaseline.VersionId(PlanCode.TripPass);
        var native = Guid.NewGuid();
        var single = Guid.NewGuid();
        foreach (var amount in new[] { 19000m, 49000m, 59000m })
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "PaymentOrders" ("Id","UserId","PlanCode","PlanVersionBinding","Amount","Type","Status",
                "ExpiresAt","ProviderOrderCode","CreatedAt","UpdatedAt")
                VALUES ({Guid.NewGuid()},{user.Id},{"TripPass"},{"LegacyUnresolved"},{amount},{"Purchase"},{"Expired"},{Now},{(long)amount},{Now},{Now})
                """);
        await using (var tx = await c.Database.BeginTransactionAsync())
        {
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "PaymentOrders" ("Id","UserId","PlanId","PlanVersionId","PlanVersionBinding",
                "Amount","Type","Status","PaidAt","ExpiresAt","ProviderOrderCode","CreatedAt","UpdatedAt")
                VALUES ({native},{user.Id},{plan},{version},{"Native"},{19000m},{"Purchase"},{"Paid"},{Now},{Now},{19001L},{Now},{Now})
                """);
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "SubscriptionPeriods" ("Id","UserId","PlanId","PlanVersionId","SourcePaymentOrderId",
                "StartsAt","EndsAt","CreatedAt","UpdatedAt")
                VALUES ({Guid.NewGuid()},{user.Id},{plan},{version},{native},{Now},{Now.AddDays(7)},{Now},{Now})
                """);
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "PaymentOrders" ("Id","UserId","ProductKind","SingleItineraryProductVersionId","CheckoutAttemptId","PlanVersionBinding",
                "Amount","Type","Status","PaidAt","ExpiresAt","ProviderOrderCode","CreatedAt","UpdatedAt")
                VALUES ({single},{user.Id},{"SingleItinerary"},{SingleItineraryBaseline.VersionId},{Guid.NewGuid()},NULL,
                {29000m},{"Purchase"},{"Paid"},{Now},{Now},{29001L},{Now},{Now})
                """);
            await c.Database.ExecuteSqlAsync($"""
                INSERT INTO "SingleItineraryEntitlements" ("Id","UserId","SourcePaymentOrderId","SingleItineraryProductVersionId","GrantedAt")
                VALUES ({Guid.NewGuid()},{user.Id},{single},{SingleItineraryBaseline.VersionId},{Now})
                """);
            await tx.CommitAsync();
        }
        var before = await c.Database.SqlQueryRaw<string>("""SELECT concat_ws('|',"Id","UserId","Amount","ProviderOrderCode","Status","PaidAt","CreatedAt","UpdatedAt") AS "Value" FROM "PaymentOrders" ORDER BY "Id" """).ToListAsync();
        const string settingKey = LocalMateAI.Application.Settings.SystemSettingKeys.MinActivePlacesPerStation;
        var setting = new SystemSetting { Key = settingKey, Value = "8", UpdatedAt = Now, UpdatedByUserId = user.Id };
        c.SystemSettings.Add(setting);
        await c.SaveChangesAsync();
        var periodsBefore = await c.Database.SqlQueryRaw<string>("""SELECT to_jsonb(p)::text AS "Value" FROM "SubscriptionPeriods" p ORDER BY "Id" """).ToListAsync();
        var entitlementsBefore = await c.Database.SqlQueryRaw<string>("""SELECT to_jsonb(e)::text AS "Value" FROM "SingleItineraryEntitlements" e ORDER BY "Id" """).ToListAsync();
        var versionsBefore = await c.Database.SqlQueryRaw<string>("""SELECT to_jsonb(v)::text AS "Value" FROM "SingleItineraryProductVersions" v ORDER BY "Id" """).ToListAsync();
        await c.Database.MigrateAsync();
        var after = await c.Database.SqlQueryRaw<string>("""SELECT concat_ws('|',"Id","UserId","Amount","ProviderOrderCode","Status","PaidAt","CreatedAt","UpdatedAt") AS "Value" FROM "PaymentOrders" ORDER BY "Id" """).ToListAsync();
        Assert.Equal(before, after);
        Assert.Equal(periodsBefore, await c.Database.SqlQueryRaw<string>("""SELECT (to_jsonb(p)-'TerminatedAt'-'TerminatedByOrderId')::text AS "Value" FROM "SubscriptionPeriods" p ORDER BY "Id" """).ToListAsync());
        Assert.Equal(entitlementsBefore, await c.Database.SqlQueryRaw<string>("""SELECT to_jsonb(e)::text AS "Value" FROM "SingleItineraryEntitlements" e ORDER BY "Id" """).ToListAsync());
        Assert.Equal(versionsBefore, await c.Database.SqlQueryRaw<string>("""SELECT to_jsonb(v)::text AS "Value" FROM "SingleItineraryProductVersions" v ORDER BY "Id" """).ToListAsync());
        var preservedSetting = await c.SystemSettings.AsNoTracking().SingleAsync();
        Assert.Equal(settingKey, preservedSetting.Key);
        Assert.Equal("8", preservedSetting.Value);
        Assert.Equal(Now, preservedSetting.UpdatedAt);
        Assert.Equal(user.Id, preservedSetting.UpdatedByUserId);
        Assert.Equal(Migration, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(c.Database.HasPendingModelChanges());
        Assert.All(await c.PaymentOrders.ToListAsync(), o => Assert.Equal(0, o.CreditAmount));
        Assert.Single(await c.SubscriptionPeriods.Where(p => p.TerminatedAt == null).ToListAsync());
        Assert.All(await c.SubscriptionPeriods.ToListAsync(), p => Assert.Null(p.TerminatedByOrderId));
        Assert.Single(await c.SingleItineraryEntitlements.ToListAsync());
        Assert.Empty(await c.PaymentOrderCredits.ToListAsync());
        Assert.DoesNotContain(await c.PaymentOrders.ToListAsync(), o => o.Type == PaymentOrderType.Upgrade);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownSafeOrFailsClosed(bool evidence)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        if (evidence)
        {
            var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
            await Upgrade(c, user.Id, 0);
            await Assert.ThrowsAsync<PostgresException>(() => c.GetService<IMigrator>().MigrateAsync(Previous));
            Assert.Equal(Migration, (await c.Database.GetAppliedMigrationsAsync()).Last());
        }
        else
        {
            await c.GetService<IMigrator>().MigrateAsync(Previous);
            Assert.Equal(Previous, (await c.Database.GetAppliedMigrationsAsync()).Last());
            await c.Database.MigrateAsync();
            Assert.False(c.Database.HasPendingModelChanges());
        }
    }

    [Fact]
    public async Task ConcurrentActiveClaims_ExactlyOneWins()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var (source, p) = await Source(c);
        var a = await Upgrade(c, source.UserId);
        var b = await Upgrade(c, source.UserId);
        async Task<bool> Attempt(Guid orderId)
        {
            await using var scope = db.Context();
            try { await InsertCredit(scope, orderId, p.Id, source.UserId, p.EndsAt); return true; }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { return false; }
        }
        var results = await Task.WhenAll(Attempt(a.Id), Attempt(b.Id));
        Assert.Single(results, x => x);
        Assert.Single(await c.PaymentOrderCredits.ToListAsync());
    }
}
