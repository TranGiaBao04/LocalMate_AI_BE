using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Controllers;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class PlanFeaturesPostgresTests
{
    private const string Foundation = "20260930114805_AddVersionedSubscriptionFoundation";
    private static readonly Guid Metro = PlanFeatureBaseline.MetroGoogleMapsId;
    private static readonly Guid TripPlan = SubscriptionBaseline.PlanId(PlanCode.TripPass);
    private static readonly Guid TripV1 = SubscriptionBaseline.VersionId(PlanCode.TripPass);
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SubscriptionPlanVersion Version(int number = 2, Guid? planId = null) => new()
    {
        PlanId = planId ?? TripPlan, VersionNumber = number, Price = 19000, DurationDays = 7,
        GenerateLimit = null, SavedTripLimit = 3, AiDailyCallLimit = 15, AiExplainCallsPerTripLimit = 3,
        Origin = PlanVersionOrigin.Published, PublishedAt = Now
    };

    private static SubscriptionService Service(AppDbContext c) => new(new UserRepository(c),
        new SubscriptionRepository(c), new UsageEventRepository(c), new TripRepository(c),
        new PlanVersionFoundationPostgresTests.Clock(Now), new LlmCallLogRepository(c), new FakeSystemSettingProvider());

    [Fact]
    public async Task FreshMigration_SeedsOnlyMetroForThreeBaselineV1s()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var repo = new SubscriptionRepository(c);
        var feature = Assert.Single(await repo.GetFeatureCatalogAsync());
        Assert.Equal(Metro, feature.Id);
        Assert.Equal("METRO_GOOGLE_MAPS", feature.Code);
        Assert.Equal("Bản đồ Metro & chỉ đường Google Maps", feature.Name);
        Assert.Null(feature.Description);
        Assert.True(feature.IsSystem);
        foreach (var version in SubscriptionBaseline.Versions())
            Assert.Equal(Metro, Assert.Single(await repo.GetFeaturesForVersionAsync(version.Id)).Id);
        Assert.Equal(3, await c.SubscriptionPlanVersionFeatures.CountAsync());
        Assert.Empty(await repo.GetFeaturesForVersionAsync(Guid.NewGuid()));
        Assert.False(c.Database.HasPendingModelChanges());
        Assert.Empty(await c.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task PublicApi_ReadsCurrentVersionFeaturesOnBothRoutes()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "ContractTests" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ISubscriptionService>(Service(c));
        builder.Services.AddSingleton<IPaymentService>(new UnusedPaymentService());
        builder.Services.AddLocalMateAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(SubscriptionController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var path in new[] { "/api/subscription/plans", "/api/subscriptions/plans" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(3, json.RootElement.GetArrayLength());
            foreach (var plan in json.RootElement.EnumerateArray())
                Assert.Equal("METRO_GOOGLE_MAPS", Assert.Single(plan.GetProperty("features").EnumerateArray())
                    .GetProperty("code").GetString());
        }
        await new SubscriptionRepository(c).PublishVersionAsync(Version(), []);
        var current = (await client.GetFromJsonAsync<SubscriptionPlanResponse[]>("/api/subscriptions/plans"))!;
        var trip = current.Single(p => p.Code == "TripPass");
        Assert.Empty(trip.Features);
        Assert.Equal(19000, trip.Price);
        Assert.Null(trip.GenerateLimit);
        Assert.Equal(3, trip.SavedTripLimit);
        Assert.Single(await new SubscriptionRepository(c).GetFeaturesForVersionAsync(TripV1));
    }

    [Fact]
    public async Task PublishV2DifferentFeatures_KeepsV1AndQuotaTerms()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var repo = new SubscriptionRepository(c);
        var v2 = Version();
        await repo.PublishVersionAsync(v2, []);
        Assert.Single(await repo.GetFeaturesForVersionAsync(TripV1));
        Assert.Empty(await repo.GetFeaturesForVersionAsync(v2.Id));
        var v3 = Version(3);
        await repo.PublishVersionAsync(v3, [Metro]);
        Assert.Single(await repo.GetFeaturesForVersionAsync(v3.Id));
        Assert.Empty(await repo.GetFeaturesForVersionAsync(v2.Id));
        Assert.Equal(v3.Id, (await repo.GetPlanAsync(TripPlan))!.CurrentVersionId);
        foreach (var v in await c.SubscriptionPlanVersions.Where(v => v.PlanId == TripPlan).ToListAsync())
        {
            Assert.Equal(19000, v.Price);
            Assert.Equal(7, v.DurationDays);
            Assert.Null(v.GenerateLimit);
            Assert.Equal(3, v.SavedTripLimit);
        }
    }

    [Fact]
    public async Task OriginalPublicationSignature_AllowsZeroQualitativeFeatures()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var repo = new SubscriptionRepository(c);
        var v2 = Version();
        await repo.PublishVersionAsync(v2);
        Assert.Empty(await repo.GetFeaturesForVersionAsync(v2.Id));
        Assert.Single(await repo.GetFeaturesForVersionAsync(TripV1));
    }

    [Fact]
    public async Task RepositoryReads_CatalogCanonicalOrder_SelectionExplicitOrder()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var fixture = new PlanFeature { Code = "TEST_QUALITATIVE", Name = "Test-only qualitative fixture" };
        c.PlanFeatures.Add(fixture);
        await c.SaveChangesAsync();
        var repo = new SubscriptionRepository(c);
        var v2 = Version();
        await repo.PublishVersionAsync(v2, [fixture.Id, Metro]);
        Assert.Equal(new[] { "METRO_GOOGLE_MAPS", "TEST_QUALITATIVE" },
            (await repo.GetFeatureCatalogAsync()).Select(f => f.Code));
        Assert.Equal(new[] { fixture.Id, Metro }, (await repo.GetFeaturesForVersionAsync(v2.Id)).Select(f => f.Id));
        Assert.Equal(new[] { 0, 1 }, await c.SubscriptionPlanVersionFeatures.Where(f => f.PlanVersionId == v2.Id)
            .OrderBy(f => f.SortOrder).Select(f => f.SortOrder).ToArrayAsync());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public async Task Publication_InvalidSelectionLeavesNoVersionAssociationsOrPointerChange(string kind)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var v2 = Version();
        await Assert.ThrowsAsync<ArgumentException>(() => new SubscriptionRepository(c)
            .PublishVersionAsync(v2, kind == "duplicate" ? [Metro, Metro] : [Guid.NewGuid()]));
        await using var read = db.Context();
        Assert.Equal(TripV1, (await new SubscriptionRepository(read).GetPlanAsync(TripPlan))!.CurrentVersionId);
        Assert.False(await read.SubscriptionPlanVersions.AnyAsync(v => v.Id == v2.Id));
        Assert.False(await read.SubscriptionPlanVersionFeatures.AnyAsync(f => f.PlanVersionId == v2.Id));
    }

    [Fact]
    public async Task Publication_PointerFailureRollsBackPreviouslyInsertedVersionAndFeatures()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        await c.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION test_fail_publication() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Isolated test failure' USING ERRCODE='23514'; END $$;
            CREATE TRIGGER test_fail_publication BEFORE UPDATE OF "CurrentVersionId" ON "SubscriptionPlans"
            FOR EACH ROW EXECUTE FUNCTION test_fail_publication();
            """);
        var v2 = Version();
        await Assert.ThrowsAsync<DbUpdateException>(() => new SubscriptionRepository(c).PublishVersionAsync(v2, [Metro]));
        await using var read = db.Context();
        Assert.Equal(TripV1, (await new SubscriptionRepository(read).GetPlanAsync(TripPlan))!.CurrentVersionId);
        Assert.False(await read.SubscriptionPlanVersions.AnyAsync(v => v.Id == v2.Id));
        Assert.False(await read.SubscriptionPlanVersionFeatures.AnyAsync(f => f.PlanVersionId == v2.Id));
        Assert.Equal(3, await read.SubscriptionPlanVersionFeatures.CountAsync());
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("append")]
    public async Task EfGuard_RejectsChangesToEstablishedVersionFeatures(string change)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var association = await c.SubscriptionPlanVersionFeatures.FirstAsync(f => f.PlanVersionId == TripV1);
        if (change == "update") c.Entry(association).Property(f => f.SortOrder).CurrentValue = 9;
        else if (change == "delete") c.Remove(association);
        else c.SubscriptionPlanVersionFeatures.Add(new()
        { PlanVersionId = TripV1, FeatureId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
        await using var read = db.Context();
        Assert.Equal(0, (await read.SubscriptionPlanVersionFeatures.SingleAsync(f => f.PlanVersionId == TripV1)).SortOrder);
        Assert.Equal(3, await read.SubscriptionPlanVersionFeatures.CountAsync());
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("append")]
    public async Task PostgresGuard_RejectsRawChangesAfterVersionCommit(string change)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var version = Version();
        await new SubscriptionRepository(c).PublishVersionAsync(version, change == "append" ? [] : [Metro]);
        var sql = change switch
        {
            "update" => $"UPDATE \"SubscriptionPlanVersionFeatures\" SET \"SortOrder\"=1 WHERE \"PlanVersionId\"='{version.Id}'",
            "delete" => $"DELETE FROM \"SubscriptionPlanVersionFeatures\" WHERE \"PlanVersionId\"='{version.Id}'",
            _ => $"INSERT INTO \"SubscriptionPlanVersionFeatures\" VALUES ('{version.Id}','{Metro}',0)"
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(change == "append" ? 0 : 1,
            await c.SubscriptionPlanVersionFeatures.CountAsync(f => f.PlanVersionId == version.Id));
    }

    [Fact]
    public async Task PostgresGuard_TruncateCannotEraseHistoricalFeatureAssociations()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            c.Database.ExecuteSqlRawAsync("TRUNCATE \"SubscriptionPlanVersionFeatures\""));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(3, await c.SubscriptionPlanVersionFeatures.CountAsync());
    }

    [Theory]
    [InlineData("duplicate", "23505")]
    [InlineData("feature_fk", "23503")]
    [InlineData("version_fk", "23503")]
    [InlineData("negative_order", "23514")]
    public async Task PostgresConstraints_RejectInvalidAssociations(string change, string state)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        await using var transaction = await c.Database.BeginTransactionAsync();
        var version = Version();
        c.SubscriptionPlanVersions.Add(version);
        c.SubscriptionPlanVersionFeatures.Add(new() { PlanVersionId = version.Id, FeatureId = Metro });
        await c.SaveChangesAsync();
        var versionId = change == "version_fk" ? Guid.NewGuid() : version.Id;
        var featureId = change == "feature_fk" ? Guid.NewGuid() : Metro;
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"SubscriptionPlanVersionFeatures\" (\"PlanVersionId\",\"FeatureId\",\"SortOrder\") VALUES ({versionId},{featureId},{(change == "negative_order" ? -1 : 0)})"));
        Assert.Equal(state, error.SqlState);
        await transaction.RollbackAsync();
        await using var read = db.Context();
        Assert.Equal(3, await read.SubscriptionPlanVersionFeatures.CountAsync());
    }

    [Theory]
    [InlineData("lowercase")]
    [InlineData("HAS SPACE")]
    [InlineData("1INVALID")]
    [InlineData("TOO_LONG")]
    [InlineData("duplicate")]
    [InlineData("empty_name")]
    public async Task FeatureCatalog_EnforcesCanonicalUniqueCodeAndName(string input)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var code = input switch { "TOO_LONG" => new string('A', 65), "duplicate" => "METRO_GOOGLE_MAPS",
            "empty_name" => "VALID_CODE", _ => input };
        c.PlanFeatures.Add(new() { Code = code, Name = input == "empty_name" ? " " : "Test feature" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
        Assert.IsType<PostgresException>(error.InnerException);
        await using var read = db.Context();
        Assert.Single(await read.PlanFeatures.ToListAsync());
    }

    [Fact]
    public async Task FeatureDelete_IsRestrictedWhileVersionReferencesIt()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"PlanFeatures\" WHERE \"Id\"={Metro}"));
        Assert.Equal("23503", error.SqlState);
    }

    [Fact]
    public async Task PostgresStamp_CannotBeOverriddenToAppendFeaturesInLaterTransaction()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var v2 = Version();
        c.SubscriptionPlanVersions.Add(v2);
        c.Entry(v2).Property<long>("FeaturePublicationTransactionId").CurrentValue = long.MaxValue;
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var stamp = await c.SubscriptionPlanVersions.Where(v => v.Id == v2.Id)
            .Select(v => EF.Property<long>(v, "FeaturePublicationTransactionId")).SingleAsync();
        Assert.NotEqual(long.MaxValue, stamp);
        var error = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"SubscriptionPlanVersionFeatures\" VALUES ({v2.Id},{Metro},0)"));
        Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task PublishConcurrentVersions_SerializesWholeFeatureSetAndPointer()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        var a = Version();
        var b = Version();
        async Task<bool> Publish(SubscriptionPlanVersion version, Guid[] ids)
        {
            await using var c = db.Context();
            try { await new SubscriptionRepository(c).PublishVersionAsync(version, ids); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(Publish(a, [Metro]), Publish(b, []));
        Assert.Single(results, success => success);
        await using var read = db.Context();
        var current = (await new SubscriptionRepository(read).GetPlanAsync(TripPlan))!.CurrentVersionId;
        Assert.Equal(results[0] ? a.Id : b.Id, current);
        Assert.Equal(results[0] ? 1 : 0,
            (await new SubscriptionRepository(read).GetFeaturesForVersionAsync(current!.Value)).Count);
        Assert.Equal(2, await read.SubscriptionPlanVersions.CountAsync(v => v.PlanId == TripPlan));
    }

    [Fact]
    public async Task UpgradeFromFoundation_PreservesFinancialPeriodsUsageAndRbac()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: Foundation);
        await using var c = db.Context();
        Assert.Equal(Foundation, (await c.Database.GetAppliedMigrationsAsync()).Last());
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var periodId = Guid.NewGuid();
        var paidOrderId = Guid.NewGuid();
        await using (var tx = await c.Database.BeginTransactionAsync())
        {
            await c.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PaymentOrders" ("Id","UserId","PlanCode","Type","Amount","Status","ProviderOrderCode","ExpiresAt","PaidAt","CheckoutUrl","QrCode","CreatedAt","UpdatedAt","PlanId","PlanVersionId","PlanVersionBinding")
                VALUES ({paidOrderId},{user.Id},'TripPass','Purchase',19000,'Paid',7701,{Now.AddMinutes(15)},{Now},'test-url','test-qr',{Now},{Now},{TripPlan},{TripV1},'Native');
                INSERT INTO "SubscriptionPeriods" ("Id","UserId","PlanId","PlanVersionId","StartsAt","EndsAt","SourcePaymentOrderId","CreatedAt","UpdatedAt")
                VALUES ({periodId},{user.Id},{TripPlan},{TripV1},{Now},{Now.AddDays(7)},{paidOrderId},{Now},{Now});
                """);
            await tx.CommitAsync();
        }
        foreach (var (amount, code, status) in new[]
        {
            (49000, "TripPass", "Pending"), (19000, "TripPass", "Failed"),
            (19000, "TripPass", "Expired"), (59000, "Membership", "Paid")
        })
            await c.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PaymentOrders" ("Id","UserId","PlanCode","Type","Amount","Status","ExpiresAt","PaidAt","CreatedAt","UpdatedAt","PlanVersionBinding")
                VALUES ({Guid.NewGuid()},{user.Id},{code},'Purchase',{amount},{status},{Now.AddMinutes(15)},{(status == "Paid" ? (DateTime?)Now : null)},{Now},{Now},'LegacyUnresolved');
                """);
        c.UserSubscriptions.Add(new() { UserId = user.Id, PlanCode = PlanCode.Membership,
            StartsAt = Now.AddDays(-30), EndsAt = Now });
        c.UsageEvents.Add(new() { UserId = user.Id, TripId = trip.Id, Type = UsageEventType.Generate,
            SubscriptionPeriodId = periodId });
        await c.SaveChangesAsync();
        var preexistingV2 = Guid.NewGuid();
        await c.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubscriptionPlanVersions" ("Id","PlanId","VersionNumber","Price","DurationDays","GenerateLimit","SavedTripLimit","Origin","PublishedAt","CreatedAt","UpdatedAt")
            VALUES ({preexistingV2},{TripPlan},2,21000,9,5,4,'Published',{Now},{Now},{Now});
            UPDATE "SubscriptionPlans" SET "CurrentVersionId"={preexistingV2} WHERE "Id"={TripPlan};
            """);
        var tables = new[] { "PaymentOrders", "SubscriptionPeriods", "UserSubscriptions", "UsageEvents",
            "Users", "Roles", "RolePermissions", "SubscriptionPlans", "SubscriptionPlanVersions" };
        async Task<string> Snapshot(string table)
        {
            // Identifiers cannot be SQL parameters; only these fixed test tables are allowed.
            if (!tables.Contains(table, StringComparer.Ordinal)) throw new ArgumentException("Unknown test table.");
            var evidence = "to_jsonb(t)-'FeaturePublicationTransactionId'-'ProductKind'-'CheckoutAttemptId'-'SingleItineraryProductVersionId'-'CreditAmount'-'TerminatedAt'-'TerminatedByOrderId'-'LockedByUserId'";
            if (table == "SubscriptionPlanVersions")
                evidence += "-'AiDailyCallLimit'-'AiExplainCallsPerTripLimit'";
            var sql = $"SELECT COALESCE(jsonb_agg({evidence} ORDER BY ({evidence})::text),'[]'::jsonb)::text AS \"Value\" FROM \"{table}\" t";
            return await c.Database.SqlQueryRaw<string>(sql).SingleAsync();
        }
        var before = new Dictionary<string, string>();
        foreach (var table in tables) before[table] = await Snapshot(table);
        await c.Database.MigrateAsync();
        foreach (var table in tables) Assert.Equal(before[table], await Snapshot(table));
        Assert.All(await c.PaymentOrders.ToListAsync(), o => Assert.Equal(0, o.CreditAmount));
        Assert.All(await c.SubscriptionPeriods.ToListAsync(), p => { Assert.Null(p.TerminatedAt); Assert.Null(p.TerminatedByOrderId); });
        Assert.Single(await new SubscriptionRepository(c).GetFeatureCatalogAsync());
        Assert.Equal(3, await c.SubscriptionPlanVersionFeatures.CountAsync());
        Assert.Empty(await new SubscriptionRepository(c).GetFeaturesForVersionAsync(preexistingV2));
        Assert.Equal(preexistingV2, (await new SubscriptionRepository(c).GetPlanAsync(TripPlan))!.CurrentVersionId);
        Assert.Equal(49000, (await c.PaymentOrders.SingleAsync(o => o.Status == PaymentOrderStatus.Pending)).Amount);
        Assert.False(c.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task FeatureVersionChange_DoesNotAlterPurchasedPeriodOrSettlementSnapshot()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var v1 = await new SubscriptionRepository(c).GetVersionAsync(TripV1);
        var order = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, TripPlan, v1!);
        var v2 = Version();
        await new SubscriptionRepository(c).PublishVersionAsync(v2, []);
        var settlement = PlanVersionFoundationPostgresTests.Settlement(c, new PlanVersionFoundationPostgresTests.Clock(Now));
        var notification = new LocalMateAI.Application.Payments.VerifiedPaymentNotification(order.ProviderOrderCode, 19000, true);
        Assert.Equal(LocalMateAI.Application.Payments.PaymentSettlementStatus.Settled,
            (await settlement.ApplyVerifiedPaymentAsync(notification)).Status);
        Assert.Equal(LocalMateAI.Application.Payments.PaymentSettlementStatus.AlreadyPaid,
            (await settlement.ApplyVerifiedPaymentAsync(notification)).Status);
        var period = await c.SubscriptionPeriods.SingleAsync();
        Assert.Equal(TripV1, period.PlanVersionId);
        Assert.Equal(Now.AddDays(7), period.EndsAt);
        Assert.Single(await new SubscriptionRepository(c).GetFeaturesForVersionAsync(period.PlanVersionId));
        Assert.Empty(await new SubscriptionRepository(c).GetFeaturesForVersionAsync(v2.Id));
        var me = (await Service(c).GetMySubscriptionAsync(user.Id))!;
        Assert.Equal("TripPass", me.Plan);
        Assert.Null(me.Usage.GenerateLimit);
        Assert.Equal(3, me.SavedTrips.Limit);
        Assert.Equal(19000, (await c.PaymentOrders.AsNoTracking().SingleAsync()).Amount);
    }

    private sealed class UnusedPaymentService : IPaymentService
    {
        public Task<CheckoutQuoteResult> GetCheckoutQuoteAsync(Guid userId, string? planCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PaymentIntentResult> CheckoutAsync(Guid userId, string? code, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<PaymentIntentResult> RenewAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<PaymentOrderLookupResult> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
