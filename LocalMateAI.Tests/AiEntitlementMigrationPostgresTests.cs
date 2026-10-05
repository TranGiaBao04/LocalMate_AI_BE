using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class AiEntitlementMigrationPostgresTests
{
    private const string Previous = "20261005084944_AddTripAiExplainedAt";
    private const string Latest = "20261005143825_AddVersionedAiEntitlements";

    [Fact]
    public async Task FreshMigration_BackfillsBuiltinsAndPreservesProtection()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        Assert.Equal(Latest, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(c.Database.HasPendingModelChanges());
        foreach (var baseline in SubscriptionBaseline.Versions())
        {
            var version = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == baseline.Id);
            Assert.Equal(baseline.AiDailyCallLimit, version.AiDailyCallLimit);
            Assert.Equal(baseline.AiExplainCallsPerTripLimit, version.AiExplainCallsPerTripLimit);
        }
        await AssertProtected(c);
    }

    [Fact]
    public async Task HistoricalUpgrade_BackfillsAllOriginsOnlyForCanonicalCodes_PreservesPurchasedEvidence_AndDownUp()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: Previous);
        await using var c = db.Context();
        await c.Database.ExecuteSqlRawAsync("""
            INSERT INTO "SubscriptionPlanVersions"
                ("Id","PlanId","VersionNumber","Price","DurationDays","GenerateLimit","SavedTripLimit",
                 "Origin","PublishedAt","CreatedAt","UpdatedAt")
            SELECT gen_random_uuid(), "PlanId", n, "Price", "DurationDays", "GenerateLimit", "SavedTripLimit",
                CASE n WHEN 2 THEN 'Published' ELSE 'LegacyReconstructed' END,
                CASE n WHEN 2 THEN now() ELSE NULL END, now(), now()
            FROM "SubscriptionPlanVersions" CROSS JOIN generate_series(2,3) n WHERE "VersionNumber"=1;
            INSERT INTO "SubscriptionPlans"
                ("Id","Code","Name","IsSystem","IsActive","CurrentVersionId","EntitlementPriority","CreatedAt","UpdatedAt")
            VALUES ('90000000-0000-0000-0000-000000000001','CUSTOM_AI','Membership',false,false,NULL,300,now(),now());
            INSERT INTO "SubscriptionPlanVersions"
                ("Id","PlanId","VersionNumber","Price","DurationDays","GenerateLimit","SavedTripLimit",
                 "Origin","PublishedAt","CreatedAt","UpdatedAt")
            SELECT gen_random_uuid(),'90000000-0000-0000-0000-000000000001',n,59000,30,NULL,NULL,
                CASE n WHEN 1 THEN 'LegacyBaseline' WHEN 2 THEN 'Published' ELSE 'LegacyReconstructed' END,
                CASE n WHEN 2 THEN now() ELSE NULL END,now(),now() FROM generate_series(1,3) n;
            """);
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var versionId = SubscriptionBaseline.VersionId(PlanCode.TripPass);
        var version = new SubscriptionPlanVersion { Id = versionId, PlanId = SubscriptionBaseline.PlanId(PlanCode.TripPass),
            Price = 19000, DurationDays = 7 };
        var start = AdminPlanServiceTests.Now;
        Guid periodId;
        await using (var tx = await c.Database.BeginTransactionAsync())
        {
            var order = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, version.PlanId, version);
            var period = new SubscriptionPeriod { UserId = user.Id, PlanId = version.PlanId, PlanVersionId = versionId,
                SourcePaymentOrderId = order.Id, StartsAt = start, EndsAt = start.AddDays(7) };
            periodId = period.Id;
            c.SubscriptionPeriods.Add(period);
            await c.SaveChangesAsync();
            await c.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"PaymentOrders\" SET \"Status\"='Paid', \"PaidAt\"={start} WHERE \"Id\"={order.Id}");
            await tx.CommitAsync();
        }
        var versionsBefore = await VersionEvidence(c, false);
        var periodsBefore = await TableEvidence(c, "SubscriptionPeriods");
        var ordersBefore = await TableEvidence(c, "PaymentOrders");
        var protectionBefore = await Protection(c);
        await c.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(versionsBefore, await VersionEvidence(c, true));
        Assert.Equal(periodsBefore, await TableEvidence(c, "SubscriptionPeriods"));
        Assert.Equal(ordersBefore, await TableEvidence(c, "PaymentOrders"));
        Assert.Equal(protectionBefore, await Protection(c));
        Assert.Equal(versionId, (await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId)).PlanVersionId);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"SubscriptionPeriods\" SET \"EndsAt\"=\"EndsAt\" + interval '1 day' WHERE \"Id\"={periodId}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"SubscriptionPlanVersionFeatures\" SET \"SortOrder\"=99 WHERE \"PlanVersionId\"={versionId}"))).SqlState);
        var rows = await (from v in c.SubscriptionPlanVersions.AsNoTracking()
            join p in c.SubscriptionPlans on v.PlanId equals p.Id
            select new { p.Code, v.AiDailyCallLimit, v.AiExplainCallsPerTripLimit }).ToListAsync();
        Assert.Equal(12, rows.Count);
        foreach (var row in rows)
        {
            var expected = row.Code switch
            { "FREE" => (3,1), "TRIP_PASS" => (15,3), "MEMBERSHIP" => (30,3), _ => ((int?,int?))(null,null) };
            Assert.Equal(expected.Item1, row.AiDailyCallLimit);
            Assert.Equal(expected.Item2, row.AiExplainCallsPerTripLimit);
        }
        await AssertProtected(c);
        await c.GetService<IMigrator>().MigrateAsync(Previous);
        Assert.Equal(Previous, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(versionsBefore, await VersionEvidence(c, false));
        Assert.Equal(protectionBefore, await Protection(c));
        Assert.Equal(periodsBefore, await TableEvidence(c, "SubscriptionPeriods"));
        await c.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(Latest, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(c.Database.HasPendingModelChanges());
        Assert.Equal(ordersBefore, await TableEvidence(c, "PaymentOrders"));
        await AssertProtected(c);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1000, 20, true)]
    [InlineData(-1, 3, false)]
    [InlineData(15, -1, false)]
    [InlineData(1001, 3, false)]
    [InlineData(15, 21, false)]
    [InlineData(null, 3, false)]
    [InlineData(15, null, false)]
    public async Task Database_PublicationRequiresAiTermsAndBounds(int? daily, int? explain, bool valid)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        async Task Insert() => await c.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubscriptionPlanVersions"
                ("Id","PlanId","VersionNumber","Price","DurationDays","GenerateLimit","SavedTripLimit",
                 "AiDailyCallLimit","AiExplainCallsPerTripLimit","Origin","PublishedAt","CreatedAt","UpdatedAt")
            VALUES ({Guid.NewGuid()},{SubscriptionBaseline.PlanId(PlanCode.TripPass)},2,19000,7,NULL,3,
                    {daily},{explain},'Published',now(),now(),now())
            """);
        if (valid) await Insert();
        else Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(Insert)).SqlState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminAiChanges_PublishImmutableNextVersion(bool daily)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = AdminPlanServiceTests.Service(new AdminPlanRepository(c));
        var first = (await service.CreateAsync(AdminPlanServiceTests.Create())).Response!;
        var terms = daily ? AdminPlanServiceTests.Update() with { AiDailyCallLimit = 20 }
            : AdminPlanServiceTests.Update() with { AiExplainCallsPerTripLimit = 2 };
        var next = (await service.UpdateAsync(first.Id, terms)).Response!;
        Assert.Equal(2, next.CurrentVersion!.VersionNumber);
        await service.UpdateAsync(first.Id, terms);
        await service.UpdateAsync(first.Id, terms with { Name = "Renamed" });
        Assert.Equal(2, await c.SubscriptionPlanVersions.CountAsync(v => v.PlanId == first.Id));
        var old = await c.SubscriptionPlanVersions.AsNoTracking().SingleAsync(v => v.Id == first.CurrentVersion!.Id);
        var current = await c.SubscriptionPlanVersions.AsNoTracking().SingleAsync(v => v.Id == next.CurrentVersion.Id);
        Assert.Equal(15, old.AiDailyCallLimit);
        Assert.Equal(3, old.AiExplainCallsPerTripLimit);
        Assert.Equal(daily ? 20 : 15, current.AiDailyCallLimit);
        Assert.Equal(daily ? 3 : 2, current.AiExplainCallsPerTripLimit);
    }

    [Fact]
    public async Task BackfillFailure_RollsBackColumnsAndRestoresOriginalTrigger()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: Previous);
        await using var c = db.Context();
        var before = await Protection(c);
        await c.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION lm_test_backfill_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Test-only backfill failure'; END $$;
            CREATE TRIGGER test_backfill_failure BEFORE UPDATE ON "SubscriptionPlanVersions"
            FOR EACH ROW EXECUTE FUNCTION lm_test_backfill_failure();
            """);
        await Assert.ThrowsAsync<PostgresException>(() => c.GetService<IMigrator>().MigrateAsync());
        Assert.Equal(Previous, (await c.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(before, await Protection(c));
        Assert.Equal(0, await c.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_name='SubscriptionPlanVersions' AND column_name IN ('AiDailyCallLimit','AiExplainCallsPerTripLimit')
            """).SingleAsync());
        await c.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER test_backfill_failure ON "SubscriptionPlanVersions";
            DROP FUNCTION lm_test_backfill_failure();
            """);
        await c.GetService<IMigrator>().MigrateAsync();
        await AssertProtected(c);
    }

    private static Task<string> VersionEvidence(AppDbContext c, bool aiColumns) => c.Database.SqlQueryRaw<string>(
        aiColumns ? """
        SELECT COALESCE(jsonb_agg(to_jsonb(v)-'AiDailyCallLimit'-'AiExplainCallsPerTripLimit' ORDER BY "Id"),'[]')::text AS "Value"
        FROM "SubscriptionPlanVersions" v
        """ : """
        SELECT COALESCE(jsonb_agg(to_jsonb(v) ORDER BY "Id"),'[]')::text AS "Value" FROM "SubscriptionPlanVersions" v
        """).SingleAsync();

    private static Task<string> TableEvidence(AppDbContext c, string table) =>
        c.Database.SqlQueryRaw<string>(table switch
        {
            "SubscriptionPeriods" => """SELECT COALESCE(jsonb_agg(to_jsonb(p) ORDER BY "Id"),'[]')::text AS "Value" FROM "SubscriptionPeriods" p""",
            "PaymentOrders" => """SELECT COALESCE(jsonb_agg(to_jsonb(p) ORDER BY "Id"),'[]')::text AS "Value" FROM "PaymentOrders" p""",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        }).SingleAsync();

    private static Task<string> Protection(AppDbContext c) => c.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object(
            'function',pg_get_functiondef('lm_immutable_subscription_row()'::regprocedure),
            'triggers',(SELECT jsonb_agg(pg_get_triggerdef(oid) ORDER BY tgname)
                        FROM pg_trigger WHERE tgfoid='lm_immutable_subscription_row()'::regprocedure))::text AS "Value"
        """).SingleAsync();

    private static async Task AssertProtected(AppDbContext c)
    {
        var id = SubscriptionBaseline.VersionId(PlanCode.TripPass);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPlanVersions\" SET \"AiDailyCallLimit\"=99 WHERE \"Id\"={id}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() =>
            c.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"SubscriptionPlanVersions\" WHERE \"Id\"={id}"))).SqlState);
    }
}
