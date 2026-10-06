using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class AiUsageAdmissionPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private const string Before = "20261005151706_AddPlaceGooglePlaceId";

    [Theory]
    [InlineData(PlanCode.Free, 10, 3)]
    [InlineData(PlanCode.Membership, 31, 30)]
    [InlineData(PlanCode.TripPass, 16, 15)]
    public async Task B01_B02_B08_B12_SeparateInstances_EnforceDailyCap(PlanCode plan, int requests, int admitted)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(seed);
        if (plan != PlanCode.Free) await PaidPeriod(seed, user.Id, plan, Now.AddDays(-1), Now.AddDays(5));
        var results = await ParallelAdmissions(db, user.Id, requests);
        Assert.Equal(admitted, results.Count(r => r.Decision.Status == AiUsageStatus.Allowed));
        Assert.Equal(requests - admitted, results.Count(r => r.Decision.Status == AiUsageStatus.DailyLimitReached));
        Assert.Equal(admitted, await seed.AiUsageAdmissions.CountAsync(a => a.State == AiUsageAdmissionState.Reserved));
        Assert.All(results.Where(r => r.Admission is not null), r =>
        {
            Assert.Equal(admitted, r.Admission!.AdmittedDailyLimit);
            Assert.Equal(SubscriptionBaseline.VersionId(plan), r.Admission.ResolvedPlanVersionId);
        });
    }

    [Fact]
    public async Task B03_FreeExplain_TwoParallelAdmissions_OneTripHold()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var results = await ParallelAdmissions(db, user.Id, 2, trip.Id);
        Assert.Single(results, r => r.Decision.Status == AiUsageStatus.Allowed);
        Assert.Single(results, r => r.Decision.Status == AiUsageStatus.TripLimitReached);
        Assert.Equal(1, await c.AiUsageAdmissions.CountAsync());
    }

    [Fact]
    public async Task B04_TripLimitPrecedesDailyLimit_WithLegacySuccess()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        await Log(c, user.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, trip.Id);
        await Log(c, user.Id, LlmCallKind.ParseRequest, LlmCallOutcome.ProviderFailed);
        await Log(c, user.Id, LlmCallKind.ParseRequest, LlmCallOutcome.InvalidOutput);
        Assert.Equal(AiUsageStatus.TripLimitReached,
            (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Decision.Status);
        Assert.Equal(AiUsageStatus.DailyLimitReached,
            (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task B05_B06_ZeroIsAuthoritative(bool explain)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        await FreeVersion(c, explain ? 3 : 0, explain ? 0 : 1);
        var result = await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id,
            explain ? LlmCallKind.Explain : LlmCallKind.ParseRequest, explain ? trip.Id : null);
        Assert.Equal(explain ? AiUsageStatus.TripLimitReached : AiUsageStatus.DailyLimitReached, result.Decision.Status);
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
    }

    [Theory]
    [InlineData(null, null, 4, 2)]
    [InlineData(null, 1, 4, 1)]
    [InlineData(7, null, 7, 2)]
    public async Task B07_LegacyNullFields_UseIndependentFallback(int? daily, int? explain, int expectedDaily, int expectedExplain)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var plan = new SubscriptionPlan { Code = "LEGACY_AI", Name = "Legacy AI", IsActive = true, EntitlementPriority = 400 };
        c.SubscriptionPlans.Add(plan);
        await c.SaveChangesAsync();
        var version = new SubscriptionPlanVersion { PlanId = plan.Id, VersionNumber = 1, Price = 19000,
            DurationDays = 7, Origin = PlanVersionOrigin.LegacyReconstructed,
            AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain };
        c.SubscriptionPlanVersions.Add(version);
        await c.SaveChangesAsync();
        await NativePeriod(c, user.Id, version, Now, Now.AddDays(7));
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.AiDailyCallsPerUser, 4)
            .Set(SystemSettingKeys.AiExplainCallsPerTrip, 2);
        var result = await new AiUsageAdmissionRepository(c, settings, new Clock(Now))
            .AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id);
        Assert.Equal(expectedDaily, result.Admission!.AdmittedDailyLimit);
        Assert.Equal(expectedExplain, result.Admission.AdmittedExplainLimit);
        Assert.Equal(version.Id, result.Admission.ResolvedPlanVersionId);
    }

    [Fact]
    public async Task B09_BrokenFreeResolution_DoesNotReserveOrFallback()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        await using var failing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(new ResolutionFailureInterceptor()).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(failing)
            .AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest));
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
    }

    [Fact]
    public async Task B10_UpgradeSameDay_PreservesReservedUsageAndAdmissionSnapshot()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        for (var i = 0; i < 3; i++) Assert.NotNull((await Repository(c)
            .AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission);
        await PaidPeriod(c, user.Id, PlanCode.Membership, Now, Now.AddDays(30));
        var result = await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(30, result.Admission!.AdmittedDailyLimit);
        Assert.Equal(4, await c.AiUsageAdmissions.CountAsync());
        Assert.Equal(3, await c.AiUsageAdmissions.CountAsync(a => a.AdmittedDailyLimit == 3));
    }

    [Fact]
    public async Task B11_ExpiryToFree_PreservesTenReservedSlotsAndBlocksLowerCap()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        await PaidPeriod(c, user.Id, PlanCode.Membership, Now.AddDays(-1), Now.AddSeconds(1));
        var clock = new Clock(Now);
        var repo = Repository(c, clock);
        for (var i = 0; i < 10; i++) await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        clock.Now = Now.AddSeconds(1);
        Assert.Equal(AiUsageStatus.DailyLimitReached,
            (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.Equal(10, await c.AiUsageAdmissions.CountAsync());
    }

    [Fact]
    public async Task B13_B14_ExpiredReserved_ReleasesExactlyOnce_AndFencesStaleDispatcher()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var clock = new Clock(Now);
        var a = (await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Admission!;
        Assert.Equal(Now.AddSeconds(30), a.ReservedUntil);
        await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(AiUsageStatus.DailyLimitReached,
            (await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.False(await Repository(c, clock).ReleaseReservedAsync(user.Id, a.Id, a.FencingToken, 1, expiredOnly: true));
        clock.Now = a.ReservedUntil;
        Assert.False(await Repository(c, clock).AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var other = db.Context();
            await gate.Task;
            return await Repository(other, clock).ReleaseReservedAsync(user.Id, a.Id, a.FencingToken, 1, expiredOnly: true);
        }).ToArray();
        gate.SetResult();
        Assert.Single(await Task.WhenAll(tasks), success => success);
        Assert.False(await Repository(c, clock).AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1));
        var stored = await c.AiUsageAdmissions.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal(2, stored.FencingGeneration);
        Assert.NotEqual(a.FencingToken, stored.FencingToken);
        Assert.NotNull((await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Admission);
    }

    [Fact]
    public async Task B15_DispatchAuthorized_IsChargedAndCannotBeReleasedOrAuthorizedTwice()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        var repo = Repository(c, clock);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        Assert.True(await repo.AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1));
        Assert.False(await repo.AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1));
        clock.Now = Now.AddMinutes(1);
        Assert.False(await repo.ReleaseReservedAsync(user.Id, a.Id, a.FencingToken, 1));
        Assert.False(await repo.ReleaseReservedAsync(user.Id, a.Id, a.FencingToken, 1, expiredOnly: true));
        await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(AiUsageStatus.DailyLimitReached,
            (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
    }

    [Fact]
    public async Task B16_AttemptAndLogLinkageAreUnique_HybridAccountingCountsCompletedOnce()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var repo = Repository(c);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AdmitAsync(a.Id, user.Id, LlmCallKind.ParseRequest));
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"AiUsageAdmissions\" SELECT * FROM \"AiUsageAdmissions\" WHERE \"Id\"={a.Id}"))).SqlState);
        var log = await Log(c, user.Id, LlmCallKind.ParseRequest, LlmCallOutcome.ProviderFailed);
        await CompleteFixture(c, a, log);
        var b = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => CompleteFixture(c, b, log))).SqlState);
        Assert.NotNull((await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission);
        Assert.Equal(AiUsageStatus.DailyLimitReached,
            (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.Equal(3, await c.AiUsageAdmissions.CountAsync());
    }

    [Theory]
    [InlineData(LlmCallOutcome.Succeeded, AiUsageStatus.TripLimitReached)]
    [InlineData(LlmCallOutcome.ProviderFailed, AiUsageStatus.Allowed)]
    [InlineData(LlmCallOutcome.InvalidOutput, AiUsageStatus.Allowed)]
    public async Task CompletedExplain_HybridTripAccounting_OnlySuccessRetainsTripCapacity(LlmCallOutcome outcome, AiUsageStatus expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var a = (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Admission!;
        await CompleteFixture(c, a, await Log(c, user.Id, LlmCallKind.Explain, outcome, trip.Id));
        Assert.Equal(expected, (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Decision.Status);
    }

    [Fact]
    public async Task AdmissionMidnight_IsImmutable_EvenIfLinkedLogCompletesNextDay()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var beforeMidnight = new DateTime(2026, 10, 6, 16, 59, 59, DateTimeKind.Utc);
        var clock = new Clock(beforeMidnight);
        var a = (await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        var log = await Log(c, user.Id, LlmCallKind.ParseRequest, LlmCallOutcome.Succeeded, at: beforeMidnight.AddSeconds(2));
        await CompleteFixture(c, a, log, beforeMidnight.AddSeconds(2));
        clock.Now = beforeMidnight.AddSeconds(2);
        for (var i = 0; i < 3; i++) Assert.NotNull((await Repository(c, clock)
            .AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission);
        Assert.Equal(new DateOnly(2026, 10, 6), (await c.AiUsageAdmissions.AsNoTracking().SingleAsync(x => x.Id == a.Id)).VietnamUsageDate);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await Repository(c, clock)
            .AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
    }

    [Fact]
    public async Task OwnershipAndFences_RejectCrossOwnerOrWrongToken()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var other = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var repo = Repository(c);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AdmitAsync(Guid.NewGuid(), other.Id, LlmCallKind.Explain, trip.Id));
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Admission!;
        Assert.False(await repo.ReleaseReservedAsync(other.Id, a.Id, a.FencingToken, 1));
        Assert.False(await repo.AuthorizeDispatchAsync(user.Id, a.Id, Guid.NewGuid(), 1));
        Assert.False(await repo.AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 2));
    }

    [Fact]
    public async Task ForeignKeys_TripRemovalRetainsSnapshot_UserRemovalCascades()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var a = (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Admission!;
        await CompleteFixture(c, a, await Log(c, user.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, trip.Id));
        await c.Trips.Where(t => t.Id == trip.Id).ExecuteDeleteAsync();
        var stored = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Null(stored.TripId);
        Assert.Equal(trip.Id, stored.TripIdSnapshot);
        await c.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        Assert.Empty(await c.AiUsageAdmissions.AsNoTracking().ToListAsync());
        Assert.Empty(await c.LlmCallLogs.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("20261005143825_AddVersionedAiEntitlements")]
    [InlineData(Before)]
    public async Task FreshAndUpgradeDownUp_PreserveLogsAiTermsAndFinancialEvidence(string previous)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(targetMigration: previous);
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        await PaidPeriod(c, user.Id, PlanCode.TripPass, Now.AddDays(-1), Now.AddDays(6), native: true);
        await Log(c, user.Id, LlmCallKind.ParseRequest, LlmCallOutcome.InvalidOutput);
        var tables = new[] { "LlmCallLogs", "SubscriptionPlanVersions", "SubscriptionPeriods", "PaymentOrders" };
        var before = new Dictionary<string, string>();
        foreach (var table in tables) before[table] = await Evidence(c, table);
        Assert.NotEqual("[]", before["PaymentOrders"]);
        Assert.NotEqual("[]", before["SubscriptionPeriods"]);
        await c.GetService<IMigrator>().MigrateAsync();
        Assert.Equal("AddAiUsageAdmissions", (await c.Database.GetAppliedMigrationsAsync()).Last()[15..]);
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
        Assert.False(c.Database.HasPendingModelChanges());
        foreach (var table in tables) Assert.Equal(before[table], await Evidence(c, table));
        await c.GetService<IMigrator>().MigrateAsync(previous);
        Assert.Equal(previous, (await c.Database.GetAppliedMigrationsAsync()).Last());
        await c.GetService<IMigrator>().MigrateAsync();
        foreach (var table in tables) Assert.Equal(before[table], await Evidence(c, table));
        Assert.False(c.Database.HasPendingModelChanges());
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
    }

    [Fact]
    public async Task DatabaseChecks_RejectImpossibleStateAndWrongAdmissionDay()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var a = (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AiUsageAdmissions\" SET \"State\"='Completed' WHERE \"Id\"={a.Id}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AiUsageAdmissions\" SET \"VietnamUsageDate\"=\"VietnamUsageDate\"+1 WHERE \"Id\"={a.Id}"))).SqlState);
    }

    [Fact]
    public async Task ClockIsCapturedOnce_AfterUserLockAcquisition()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var holder = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(holder);
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\"={user.Id} FOR UPDATE");
        var interceptor = new LockStartedInterceptor();
        await using var waiting = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(interceptor).Options);
        var clock = new Clock(Now);
        var pending = Repository(waiting, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        await interceptor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, clock.Reads);
        clock.Now = new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc);
        await tx.CommitAsync();
        var result = await pending;
        Assert.Equal(1, clock.Reads);
        Assert.Equal(clock.Now, result.Admission!.AdmittedAt);
        Assert.Equal(clock.Now.AddSeconds(30), result.Admission.ReservedUntil);
        Assert.Equal(new DateOnly(2026, 10, 7), result.Admission.VietnamUsageDate);
    }

    [Fact]
    public async Task ConcurrentReleaseAndAuthorize_OnlyOneTransitionWins()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var a = (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Run(bool release)
        {
            await using var other = db.Context();
            await gate.Task;
            return release ? await Repository(other).ReleaseReservedAsync(user.Id, a.Id, a.FencingToken, 1)
                : await Repository(other).AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1);
        }
        var release = Run(true);
        var dispatch = Run(false);
        gate.SetResult();
        var results = await Task.WhenAll(release, dispatch);
        Assert.Single(results, success => success);
        var stored = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(results[0] ? AiUsageAdmissionState.Released : AiUsageAdmissionState.DispatchAuthorized, stored.State);
    }

    private static AiUsageAdmissionRepository Repository(AppDbContext c, Clock? clock = null) =>
        new(c, new FakeSystemSettingProvider(), clock ?? new Clock(Now));

    private static async Task<AiUsageAdmissionResult[]> ParallelAdmissions(IsolatedPlanDatabase db, Guid userId, int count, Guid? tripId = null)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = 0;
        var tasks = Enumerable.Range(0, count).Select(async _ =>
        {
            await using var c = db.Context();
            await c.Database.OpenConnectionAsync();
            if (Interlocked.Increment(ref waiting) == count) ready.SetResult();
            await gate.Task;
            return await Repository(c).AdmitAsync(Guid.NewGuid(), userId,
                tripId is null ? LlmCallKind.ParseRequest : LlmCallKind.Explain, tripId);
        }).ToArray();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        gate.SetResult();
        return await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static async Task PaidPeriod(AppDbContext c, Guid userId, PlanCode plan, DateTime start, DateTime end, bool native = false)
    {
        var version = await c.SubscriptionPlanVersions.AsNoTracking().SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(plan));
        if (native)
        {
            await NativePeriod(c, userId, version, start, end);
        }
        else
        {
            var legacy = new UserSubscription { UserId = userId, PlanCode = plan, StartsAt = start, EndsAt = end };
            c.UserSubscriptions.Add(legacy);
            await c.SaveChangesAsync();
            c.SubscriptionPeriods.Add(new SubscriptionPeriod { UserId = userId, PlanId = version.PlanId,
                PlanVersionId = version.Id, LegacyUserSubscriptionId = legacy.Id, StartsAt = start, EndsAt = end });
            await c.SaveChangesAsync();
        }
    }

    private static async Task<SubscriptionPlanVersion> FreeVersion(AppDbContext c, int? daily, int? explain)
    {
        var version = new SubscriptionPlanVersion { PlanId = SubscriptionBaseline.PlanId(PlanCode.Free), VersionNumber = 2,
            Price = 0, GenerateLimit = 3, SavedTripLimit = 1, Origin = PlanVersionOrigin.Published, PublishedAt = Now,
            AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain };
        await new SubscriptionRepository(c).PublishVersionAsync(version);
        return version;
    }

    private static async Task NativePeriod(AppDbContext c, Guid userId, SubscriptionPlanVersion version, DateTime start, DateTime end)
    {
        await using var tx = await c.Database.BeginTransactionAsync();
        var order = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, userId, version.PlanId, version);
        c.SubscriptionPeriods.Add(new SubscriptionPeriod { UserId = userId, PlanId = version.PlanId,
            PlanVersionId = version.Id, SourcePaymentOrderId = order.Id, StartsAt = start, EndsAt = end });
        await c.SaveChangesAsync();
        await c.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PaymentOrders\" SET \"Status\"='Paid',\"PaidAt\"={start} WHERE \"Id\"={order.Id}");
        await tx.CommitAsync();
    }

    private static async Task<LlmCallLog> Log(AppDbContext c, Guid userId, LlmCallKind kind, LlmCallOutcome outcome,
        Guid? tripId = null, DateTime? at = null)
    {
        var log = new LlmCallLog { UserId = userId, TripId = tripId, Kind = kind, Outcome = outcome, Model = "test-only" };
        c.LlmCallLogs.Add(log);
        await c.SaveChangesAsync();
        await c.LlmCallLogs.Where(l => l.Id == log.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, at ?? Now));
        return log;
    }

    // S5B tests seed completion evidence directly; no production completion/provider integration is implied.
    private static Task CompleteFixture(AppDbContext c, AiUsageAdmission admission, LlmCallLog log, DateTime? at = null) =>
        c.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "AiUsageAdmissions" SET "State"='Completed', "DispatchAuthorizedAt"={admission.AdmittedAt},
                "CompletedAt"={at ?? admission.AdmittedAt}, "Outcome"={log.Outcome.ToString()}, "LlmCallLogId"={log.Id}
            WHERE "Id"={admission.Id}
            """);

    private static Task<string> Evidence(AppDbContext c, string table) => c.Database.SqlQueryRaw<string>(table switch
    {
        "LlmCallLogs" => """SELECT COALESCE(jsonb_agg(to_jsonb(p) ORDER BY "Id"),'[]')::text AS "Value" FROM "LlmCallLogs" p""",
        "SubscriptionPlanVersions" => """SELECT COALESCE(jsonb_agg(to_jsonb(p) ORDER BY "Id"),'[]')::text AS "Value" FROM "SubscriptionPlanVersions" p""",
        "SubscriptionPeriods" => """SELECT COALESCE(jsonb_agg(to_jsonb(p) ORDER BY "Id"),'[]')::text AS "Value" FROM "SubscriptionPeriods" p""",
        "PaymentOrders" => """SELECT COALESCE(jsonb_agg(to_jsonb(p) ORDER BY "Id"),'[]')::text AS "Value" FROM "PaymentOrders" p""",
        _ => throw new ArgumentOutOfRangeException(nameof(table))
    }).SingleAsync();

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return new(Now); }
    }

    private sealed class LockStartedInterceptor : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Users\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Started.TrySetResult();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ResolutionFailureInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"SubscriptionPeriods\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Test-only subscription resolution failure.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
