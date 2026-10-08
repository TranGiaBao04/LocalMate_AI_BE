using System.Data.Common;
using System.Text.Json;
using LocalMateAI.API.BackgroundJobs;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AiUsageRecoveryPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private static readonly AiUsageCompletion Success = new(LlmCallOutcome.Succeeded, "test", 7, 4, 1);

    [Fact]
    public async Task D01_D02_D04_D05_ExpiredReservation_ConcurrentWorkersReleaseOnce_ExpiredDispatchNeverWins()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        var repo = Repo(c, clock);
        var handle = Handle((await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!);
        for (var i = 0; i < 2; i++) await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        clock.Now = Now.AddSeconds(30);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Run(bool dispatch)
        {
            await using var other = db.Context();
            await gate.Task;
            return dispatch ? await Repo(other, clock).AuthorizeDispatchAsync(user.Id, handle.AttemptId, handle.FencingToken, handle.FencingGeneration)
                : await Repo(other, clock).ReleaseExpiredReservedAsync(handle);
        }
        var first = Run(false);
        var second = Run(false);
        var dispatch = Run(true);
        gate.SetResult();
        var results = await Task.WhenAll(first, second, dispatch).WaitAsync(Deadline);
        Assert.Single(results.Take(2), r => r);
        Assert.False(results[2]);
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync(a => a.Id == handle.AttemptId);
        Assert.Equal(AiUsageAdmissionState.Released, row.State);
        Assert.Equal(clock.Now, row.CompletedAt);
        Assert.Equal(2, row.FencingGeneration);
        Assert.NotEqual(handle.FencingToken, row.FencingToken);
        Assert.False(await repo.AuthorizeDispatchAsync(user.Id, handle.AttemptId, handle.FencingToken, 1));
        Assert.False(await repo.ReleaseExpiredReservedAsync(handle));
        Assert.Equal(2, await repo.CountDailyAsync(user.Id, DateOnly.FromDateTime(Now.AddHours(7))));
        Assert.Equal(AiUsageStatus.Allowed, (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
    }

    [Fact]
    public async Task D03_ExpiredExplainReservation_RestoresTripCapacity()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var clock = new Clock(Now);
        var repo = Repo(c, clock);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Admission!;
        Assert.False(await repo.ReleaseExpiredReservedAsync(Handle(a)));
        Assert.Equal(AiUsageStatus.TripLimitReached, (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Decision.Status);
        clock.Now = Now.AddSeconds(31);
        Assert.True(await repo.ReleaseExpiredReservedAsync(Handle(a)));
        Assert.Equal(AiUsageStatus.Allowed, (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Decision.Status);
    }

    [Fact]
    public async Task D06_D11_DispatchDeadline_AbandonsWithoutLog_KeepsDaily_ReleasesTrip_FencesCompletion()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var clock = new Clock(Now);
        var repo = Repo(c, clock);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Admission!;
        var handle = Handle(a);
        Assert.True(await repo.AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1));
        clock.Now = Now.AddMinutes(2).AddTicks(-1);
        Assert.Empty(await repo.GetExpiredAsync(AiUsageAdmissionState.DispatchAuthorized, 50));
        Assert.False(await repo.AbandonExpiredDispatchAuthorizedAsync(handle));
        Assert.Equal(AiUsageStatus.TripLimitReached, (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Decision.Status);
        clock.Now = Now.AddMinutes(2);
        Assert.Equal(handle, Assert.Single(await repo.GetExpiredAsync(AiUsageAdmissionState.DispatchAuthorized, 50)));
        Assert.True(await repo.AbandonExpiredDispatchAuthorizedAsync(handle));
        Assert.False(await repo.AbandonExpiredDispatchAuthorizedAsync(handle));
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.Abandoned, row.State);
        Assert.Equal(clock.Now, row.CompletedAt);
        Assert.Equal(Now, row.DispatchAuthorizedAt);
        Assert.Equal(2, row.FencingGeneration);
        Assert.NotEqual(handle.FencingToken, row.FencingToken);
        Assert.Null(row.RecoveryAfter);
        Assert.Null(row.LlmCallLogId);
        Assert.Null(row.Outcome);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CompleteAsync(handle, Success));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.CompleteAsync(Handle(row), Success));
        Assert.Equal(1, await repo.CountDailyAsync(user.Id, DateOnly.FromDateTime(Now.AddHours(7))));
        Assert.Equal(AiUsageStatus.Allowed, (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Decision.Status);
        await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task D12_CompletionVsAbandonment_RealLockRace_ExactlyOneTerminalWinner(bool completionFirst)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        var repo = Repo(c, clock);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        var handle = Handle(a);
        await repo.AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1);
        clock.Now = Now.AddMinutes(2);
        var pause = new TerminalPause();
        var waiting = new LockProbe();
        await using var winner = Context(db, pause);
        await using var loser = Context(db, waiting);
        var winnerTask = completionFirst ? Complete(Repo(winner, clock), handle) : Repo(winner, clock).AbandonExpiredDispatchAuthorizedAsync(handle);
        await pause.Started.Task.WaitAsync(Deadline); // Canonical user lock is held; terminal SQL has not run.
        var loserTask = completionFirst ? Repo(loser, clock).AbandonExpiredDispatchAuthorizedAsync(handle) : Complete(Repo(loser, clock), handle);
        await waiting.Started.Task.WaitAsync(Deadline);
        pause.Proceed.SetResult();
        Assert.True(await winnerTask.WaitAsync(Deadline));
        Assert.False(await loserTask.WaitAsync(Deadline));
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(completionFirst ? AiUsageAdmissionState.Completed : AiUsageAdmissionState.Abandoned, row.State);
        Assert.Equal(completionFirst ? 1 : 0, await c.LlmCallLogs.CountAsync());
        Assert.Equal(1, await repo.CountDailyAsync(user.Id, DateOnly.FromDateTime(Now.AddHours(7))));
        Assert.False(await repo.AbandonExpiredDispatchAuthorizedAsync(handle));
    }

    [Theory]
    [InlineData(LlmCallOutcome.Succeeded)]
    [InlineData(LlmCallOutcome.ProviderFailed)]
    [InlineData(LlmCallOutcome.InvalidOutput)]
    public async Task D13_D14_CompletedOutcomes_NeverRecoveredOrChanged(LlmCallOutcome outcome)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        var repo = Repo(c, clock);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        await repo.AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1);
        var log = await repo.CompleteAsync(Handle(a), Success with { Outcome = outcome });
        clock.Now = Now.AddMinutes(3);
        Assert.Empty(await repo.GetExpiredAsync(AiUsageAdmissionState.DispatchAuthorized, 50));
        Assert.Empty(await repo.GetExpiredAsync(AiUsageAdmissionState.Reserved, 50));
        Assert.False(await repo.AbandonExpiredDispatchAuthorizedAsync(Handle(a)));
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.Completed, row.State);
        Assert.Equal(outcome, row.Outcome);
        Assert.Equal(log.LlmCallLogId, row.LlmCallLogId);
        Assert.Equal(1, row.FencingGeneration);
        Assert.Equal(1, await c.LlmCallLogs.CountAsync());
    }

    [Fact]
    public async Task LateExplainAfterAbandonment_NeverLogsOrAppliesReasoning()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var place = new Place { Name = "Known cafe", Address = "Local", Description = "Known database facts",
            Location = new Point(106.5, 10.75) { SRID = 4326 }, Category = PlaceCategory.Cafe, Status = PlaceStatus.Active };
        c.ItineraryItems.Add(new ItineraryItem { TripId = trip.Id, Place = place, Reasoning = "old reason",
            ScheduledTime = new TimeOnly(10, 0), EstimatedDurationMinutes = 60 });
        await c.SaveChangesAsync();
        var clock = new Clock(Now);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<LlmJsonResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var llm = new WaitingLlm(entered, answer);
        var settings = new FakeSystemSettingProvider();
        var service = new TripExplanationService(new UserRepository(c), new TripExplanationRepository(c),
            new AiUsageCoordinator(Repo(c, clock), llm, settings, NullLogger<AiUsageCoordinator>.Instance), llm, clock);
        var pending = service.ExplainAsync(user.Id, trip.Id);
        await entered.Task.WaitAsync(Deadline);
        await using var recovery = db.Context();
        var handle = Handle(await recovery.AiUsageAdmissions.AsNoTracking().SingleAsync());
        clock.Now = Now.AddMinutes(2);
        Assert.True(await Repo(recovery, clock).AbandonExpiredDispatchAuthorizedAsync(handle));
        answer.SetResult(new LlmJsonResponse(JsonSerializer.Serialize(new
            { stops = new[] { new { placeId = place.Id, hasEnoughData = true, reason = "new AI reason" } } }), 7, 4));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(1, llm.Calls);
        Assert.Empty(await recovery.LlmCallLogs.ToListAsync());
        Assert.Null(await recovery.Trips.Where(t => t.Id == trip.Id).Select(t => t.AiExplainedAt).SingleAsync());
        Assert.Equal("old reason", await recovery.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.Reasoning).SingleAsync());
        Assert.Equal(AiUsageAdmissionState.Abandoned, (await recovery.AiUsageAdmissions.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task WorkerWithRealPostgres_RecoversBothStatesEvenWhenAiDisabled_AndScanIsBounded()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        var reserved = (await Repo(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        var dispatch = (await Repo(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        await Repo(c, clock).AuthorizeDispatchAsync(user.Id, dispatch.Id, dispatch.FencingToken, 1);
        clock.Now = Now.AddMinutes(2);
        Assert.Single(await Repo(c, clock).GetExpiredAsync(AiUsageAdmissionState.Reserved, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repo(c, clock).GetExpiredAsync(AiUsageAdmissionState.Reserved, 51));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repo(c, clock).GetExpiredAsync(AiUsageAdmissionState.Completed, 50));
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.AiEnabled, 0);
        var services = new ServiceCollection().AddScoped(_ => db.Context())
            .AddScoped<IAiUsageAdmissionRepository>(sp => new AiUsageAdmissionRepository(sp.GetRequiredService<AppDbContext>(), settings, clock));
        await using var provider = services.BuildServiceProvider();
        var worker = new AiUsageRecoveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<AiUsageRecoveryWorker>.Instance);
        await worker.RunOnceAsync();
        Assert.Equal(AiUsageAdmissionState.Released, (await c.AiUsageAdmissions.AsNoTracking().SingleAsync(a => a.Id == reserved.Id)).State);
        Assert.Equal(AiUsageAdmissionState.Abandoned, (await c.AiUsageAdmissions.AsNoTracking().SingleAsync(a => a.Id == dispatch.Id)).State);
        Assert.Empty(settings.RequestedKeys);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
    }

    [Theory]
    [InlineData(AiUsageAdmissionState.Reserved, 3)]
    [InlineData(AiUsageAdmissionState.DispatchAuthorized, 3)]
    [InlineData(AiUsageAdmissionState.Completed, 3)]
    [InlineData(AiUsageAdmissionState.Abandoned, 3)]
    [InlineData(AiUsageAdmissionState.Released, 2)]
    public async Task D17_D21_MeTwoCompletedPlusThirdState_UsesHybridExactly(AiUsageAdmissionState state, int expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.Completed);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.Completed);
        await Attempt(c, clock, user.Id, state);
        var me = (await Me(c, clock).GetMySubscriptionAsync(user.Id))!;
        Assert.Equal(expected, me.Ai.DailyUsed);
        Assert.Equal(3, me.Ai.DailyLimit);
        Assert.Equal(VietnamTime.StartOfDayUtc(clock.Now).AddDays(1), me.Ai.ResetAt);
        Assert.Equal(state == AiUsageAdmissionState.Completed ? 3 : 2, await c.LlmCallLogs.CountAsync());
        Assert.Equal(expected, await Repo(c, clock).CountDailyAsync(user.Id, DateOnly.FromDateTime(clock.Now.AddHours(7))));
    }

    [Fact]
    public async Task D16_D22_MixedLegacyAndLedger_ExactOwnerDay_AllOutcomes_HistoricalRepositoryUnchanged()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var other = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        await Paid(c, user.Id, Now.AddDays(-1), Now.AddDays(29));
        var start = VietnamTime.StartOfDayUtc(Now);
        foreach (var kind in Enum.GetValues<LlmCallKind>())
            foreach (var outcome in Enum.GetValues<LlmCallOutcome>()) await Legacy(c, user.Id, start, kind, outcome);
        await Legacy(c, other.Id, start);
        await Legacy(c, user.Id, start.AddTicks(-1));
        await Legacy(c, user.Id, start.AddDays(1));
        foreach (var state in new[] { AiUsageAdmissionState.Completed, AiUsageAdmissionState.Reserved,
                     AiUsageAdmissionState.DispatchAuthorized, AiUsageAdmissionState.Released, AiUsageAdmissionState.Abandoned })
            await Attempt(c, clock, user.Id, state);
        await c.LlmCallLogs.Where(l => c.AiUsageAdmissions.Any(a => a.LlmCallLogId == l.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, Now));
        Assert.Equal(10, (await Me(c, clock).GetMySubscriptionAsync(user.Id))!.Ai.DailyUsed); // six legacy + four ledger
        Assert.Equal(1, (await Me(c, clock).GetMySubscriptionAsync(other.Id))!.Ai.DailyUsed);
        // Generic evidence count still includes future logs and linked logs; no global semantic rewrite.
        Assert.Equal(8, await new LlmCallLogRepository(c).CountForUserSinceAsync(user.Id, start));
    }

    [Fact]
    public async Task D23_D24_AdmissionBeforeMidnight_LinkedCompletionNextDay_IsNotChargedAgain()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var before = new DateTime(2026, 10, 6, 16, 59, 59, DateTimeKind.Utc);
        var clock = new Clock(before);
        var repo = Repo(c, clock);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        await repo.AuthorizeDispatchAsync(user.Id, a.Id, a.FencingToken, 1);
        clock.Now = before.AddSeconds(2);
        var completed = await repo.CompleteAsync(Handle(a), Success);
        // Normalize wall-clock BaseEntity timestamp to the simulated real completion instant.
        await c.LlmCallLogs.Where(l => l.Id == completed.LlmCallLogId).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, clock.Now));
        Assert.Equal(1, await repo.CountDailyAsync(user.Id, new DateOnly(2026, 10, 6)));
        Assert.Equal(0, (await Me(c, clock).GetMySubscriptionAsync(user.Id))!.Ai.DailyUsed);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.Reserved);
        Assert.Equal(1, (await Me(c, clock).GetMySubscriptionAsync(user.Id))!.Ai.DailyUsed);
    }

    [Fact]
    public async Task D25_FreeToMembership_SameDayUsageRetained()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.Completed);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.Reserved);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.DispatchAuthorized);
        Assert.Equal(3, (await Me(c, clock).GetMySubscriptionAsync(user.Id))!.Ai.DailyUsed);
        await Paid(c, user.Id, Now, Now.AddDays(30));
        var me = (await Me(c, clock).GetMySubscriptionAsync(user.Id))!;
        Assert.Equal(3, me.Ai.DailyUsed);
        Assert.Equal(30, me.Ai.DailyLimit);
        Assert.Equal(AiUsageStatus.Allowed, (await Repo(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
    }

    [Fact]
    public async Task D26_MembershipExpires_ChargedTenRemainsVisibleAgainstFreeThree()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        await Paid(c, user.Id, Now.AddMinutes(5).AddDays(-30), Now.AddMinutes(5));
        var pendingAbandon = new List<AiUsageHandle>();
        for (var i = 0; i < 10; i++)
        {
            var state = i % 3 == 0 ? AiUsageAdmissionState.Completed : AiUsageAdmissionState.DispatchAuthorized;
            var handle = await Attempt(c, clock, user.Id, state);
            if (i % 3 == 2) pendingAbandon.Add(handle);
        }
        clock.Now = Now.AddMinutes(2);
        foreach (var handle in pendingAbandon) Assert.True(await Repo(c, clock).AbandonExpiredDispatchAuthorizedAsync(handle));
        clock.Now = Now.AddMinutes(5);
        var me = (await Me(c, clock).GetMySubscriptionAsync(user.Id))!;
        Assert.Equal("Free", me.Plan);
        Assert.Equal(10, me.Ai.DailyUsed);
        Assert.Equal(3, me.Ai.DailyLimit);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await Repo(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(null, 7)]
    [InlineData(3, 3)]
    public async Task D27_D28_D29_MeRealUsed_ZeroOrNullTerms_AiDisabledDoesNotZeroAccounting(int? daily, int expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new Clock(Now);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.Completed);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.DispatchAuthorized);
        if (daily is null)
        {
            // Missing AI metadata belongs to a purchased legacy/custom version, not a new publication.
            var plan = new SubscriptionPlan { Code = "LEGACY_AI", Name = "Legacy AI", IsActive = true, EntitlementPriority = 400 };
            c.SubscriptionPlans.Add(plan);
            await c.SaveChangesAsync();
            var legacy = new SubscriptionPlanVersion { PlanId = plan.Id, VersionNumber = 1, Price = 19000,
                DurationDays = 7, Origin = PlanVersionOrigin.LegacyReconstructed, AiExplainCallsPerTripLimit = 1 };
            c.SubscriptionPlanVersions.Add(legacy);
            await c.SaveChangesAsync();
            await using var tx = await c.Database.BeginTransactionAsync();
            var order = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, plan.Id, legacy);
            c.SubscriptionPeriods.Add(new SubscriptionPeriod { UserId = user.Id, PlanId = plan.Id, PlanVersionId = legacy.Id,
                SourcePaymentOrderId = order.Id, StartsAt = Now, EndsAt = Now.AddDays(7) });
            await c.SaveChangesAsync();
            await c.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PaymentOrders\" SET \"Status\"='Paid',\"PaidAt\"={Now} WHERE \"Id\"={order.Id}");
            await tx.CommitAsync();
        }
        else
            await new SubscriptionRepository(c).PublishVersionAsync(new SubscriptionPlanVersion
            {
                PlanId = SubscriptionBaseline.PlanId(PlanCode.Free), VersionNumber = 2, Price = 0, GenerateLimit = 1,
                SavedTripLimit = 1, Origin = PlanVersionOrigin.Published, PublishedAt = Now,
                AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = 1
            });
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.AiEnabled, 0)
            .Set(SystemSettingKeys.AiDailyCallsPerUser, 7);
        var me = (await Me(c, clock, settings).GetMySubscriptionAsync(user.Id))!;
        Assert.Equal(2, me.Ai.DailyUsed);
        Assert.Equal(expected, me.Ai.DailyLimit);
        Assert.DoesNotContain(SystemSettingKeys.AiEnabled, settings.RequestedKeys);
        Assert.Equal(daily is null, settings.RequestedKeys.Contains(SystemSettingKeys.AiDailyCallsPerUser));
    }

    [Fact]
    public async Task D30_MeCapturesOneClockAtMidnight_UsesSameDateAndReset()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var before = new DateTime(2026, 10, 6, 16, 59, 59, DateTimeKind.Utc);
        var clock = new Clock(before);
        await Attempt(c, clock, user.Id, AiUsageAdmissionState.Reserved);
        clock.Now = before.AddSeconds(1).AddTicks(-1);
        clock.AdvanceOnRead = true;
        clock.Reads = 0;
        var me = (await Me(c, clock).GetMySubscriptionAsync(user.Id))!;
        Assert.Equal(1, clock.Reads);
        Assert.Equal(1, me.Ai.DailyUsed);
        Assert.Equal(new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc), me.Ai.ResetAt);
    }

    private static AiUsageAdmissionRepository Repo(AppDbContext c, Clock clock) => new(c, new FakeSystemSettingProvider(), clock);
    private static SubscriptionService Me(AppDbContext c, Clock clock, FakeSystemSettingProvider? settings = null) =>
        new(new UserRepository(c), new SubscriptionRepository(c), new UsageEventRepository(c), new TripRepository(c),
            clock, Repo(c, clock), settings ?? new());
    private static AiUsageHandle Handle(AiUsageAdmission a) => new(a.Id, a.UserId, a.Kind, a.TripIdSnapshot, a.FencingToken, a.FencingGeneration);
    private static AppDbContext Context(IsolatedPlanDatabase db, DbCommandInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(interceptor).Options);

    private static async Task<bool> Complete(AiUsageAdmissionRepository repo, AiUsageHandle handle)
    {
        try { return (await repo.CompleteAsync(handle, Success)).CompletedNow; }
        catch (InvalidOperationException) { return false; }
    }

    private static async Task<AiUsageHandle> Attempt(AppDbContext c, Clock clock, Guid userId, AiUsageAdmissionState state)
    {
        var repo = Repo(c, clock);
        var a = (await repo.AdmitAsync(Guid.NewGuid(), userId, LlmCallKind.ParseRequest)).Admission
            ?? throw new InvalidOperationException("Test fixture admission unexpectedly denied.");
        var handle = Handle(a);
        if (state == AiUsageAdmissionState.Released) Assert.True(await repo.ReleaseReservedAsync(userId, a.Id, a.FencingToken, 1));
        else if (state != AiUsageAdmissionState.Reserved)
        {
            Assert.True(await repo.AuthorizeDispatchAsync(userId, a.Id, a.FencingToken, 1));
            if (state == AiUsageAdmissionState.Completed) await repo.CompleteAsync(handle, Success);
            else if (state == AiUsageAdmissionState.Abandoned)
            {
                clock.Now = clock.Now.AddMinutes(2);
                Assert.True(await repo.AbandonExpiredDispatchAuthorizedAsync(handle));
            }
        }
        return handle;
    }

    private static async Task Paid(AppDbContext c, Guid userId, DateTime start, DateTime end)
    {
        var version = await c.SubscriptionPlanVersions.AsNoTracking().SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.Membership));
        var old = new UserSubscription { UserId = userId, PlanCode = PlanCode.Membership, StartsAt = start, EndsAt = end };
        c.UserSubscriptions.Add(old);
        await c.SaveChangesAsync();
        c.SubscriptionPeriods.Add(new SubscriptionPeriod { UserId = userId, PlanId = version.PlanId, PlanVersionId = version.Id,
            LegacyUserSubscriptionId = old.Id, StartsAt = start, EndsAt = end });
        await c.SaveChangesAsync();
    }

    private static async Task Legacy(AppDbContext c, Guid userId, DateTime at, LlmCallKind kind = LlmCallKind.ParseRequest,
        LlmCallOutcome outcome = LlmCallOutcome.ProviderFailed)
    {
        var log = new LlmCallLog { UserId = userId, Kind = kind, Outcome = outcome, Model = "legacy-test" };
        c.LlmCallLogs.Add(log);
        await c.SaveChangesAsync();
        await c.LlmCallLogs.Where(l => l.Id == log.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, at));
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public int Reads { get; set; }
        public bool AdvanceOnRead { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            var value = Now;
            if (AdvanceOnRead) Now = Now.AddTicks(1);
            return new(value);
        }
    }

    private sealed class TerminalPause : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"AiUsageAdmissions\"", StringComparison.Ordinal))
            {
                Started.TrySetResult();
                await Proceed.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class LockProbe : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Users\"", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
                Started.TrySetResult();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class WaitingLlm(TaskCompletionSource entered, TaskCompletionSource<LlmJsonResponse> answer) : ILlmClient
    {
        public int Calls { get; private set; }
        public string Model => "test";
        public bool IsConfigured => true;
        public Task<LlmJsonResponse> GenerateJsonAsync(LlmJsonRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            entered.SetResult();
            return answer.Task.WaitAsync(cancellationToken);
        }
    }
}
