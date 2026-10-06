using System.Data.Common;
using System.Text.Json;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Domain.Services;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AiUsageDispatchPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
    private static readonly ParseTripRequestRequest ParseRequest = new("plan a coffee trip tomorrow");
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(PlanCode.Free, 10, 3)]
    [InlineData(PlanCode.Membership, 31, 30)]
    public async Task C01_C02_C17_IndependentParseScopes_OnlyAdmittedRequestsReachProvider(
        PlanCode plan, int requests, int limit)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var user = await User(seed, plan);
        var barrier = new ProviderBarrier(limit);
        var tasks = Enumerable.Range(0, requests).Select(async _ =>
        {
            await using var c = db.Context();
            var llm = new FakeLlm(async (_, ct) =>
            {
                Assert.Null(c.Database.CurrentTransaction);
                await barrier.EnterAsync(ct);
                return ParseAnswer;
            });
            return await Parser(c, llm).ParseAsync(user.Id, ParseRequest);
        }).ToArray();
        await barrier.Arrived.Task.WaitAsync(Deadline);
        // While provider calls are paused, every other request must be denied, not queued to Gemini.
        await WaitForCompleted(tasks, requests - limit);
        Assert.Equal(limit, barrier.Calls);
        await using (var check = db.Context())
        {
            var rows = await check.AiUsageAdmissions.AsNoTracking().ToListAsync();
            Assert.Equal(limit, rows.Count);
            Assert.All(rows, a =>
            {
                Assert.Equal(AiUsageAdmissionState.DispatchAuthorized, a.State);
                Assert.Equal(a.DispatchAuthorizedAt!.Value.AddMinutes(2), a.RecoveryAfter);
            });
            // A different connection can acquire the canonical lock while network is pending.
            await using var tx = await check.Database.BeginTransactionAsync();
            await check.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\"={user.Id} FOR UPDATE");
            await tx.CommitAsync();
        }
        barrier.Release.TrySetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(Deadline);
        Assert.Equal(limit, results.Count(r => r.Status == ParseTripRequestResultStatus.Success));
        Assert.Equal(requests - limit, results.Count(r => r.Status == ParseTripRequestResultStatus.DailyLimitReached));
        Assert.Equal(limit, await seed.LlmCallLogs.CountAsync());
        Assert.Equal(limit, await seed.AiUsageAdmissions.CountAsync(a => a.State == AiUsageAdmissionState.Completed));
    }

    [Theory]
    [InlineData(PlanCode.Free, 2, 1)]
    [InlineData(PlanCode.TripPass, 4, 3)]
    [InlineData(PlanCode.Membership, 4, 3)]
    public async Task C03_C04_ExplainParallelSuccess_CannotExceedTripAllowance(PlanCode plan, int requests, int limit)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var user = await User(seed, plan);
        var trip = await Trip(seed, user.Id);
        var place = await seed.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.PlaceId).SingleAsync();
        var barrier = new ProviderBarrier(limit);
        var tasks = Enumerable.Range(0, requests).Select(async _ =>
        {
            await using var c = db.Context();
            var llm = new FakeLlm(async (_, ct) => { await barrier.EnterAsync(ct); return ExplainAnswer(place); });
            return await Explainer(c, llm).ExplainAsync(user.Id, trip.Id);
        }).ToArray();
        await barrier.Arrived.Task.WaitAsync(Deadline);
        await WaitForCompleted(tasks, requests - limit);
        Assert.Equal(limit, barrier.Calls);
        barrier.Release.TrySetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(Deadline);
        Assert.Equal(limit, results.Count(r => r.Status == ExplainTripResultStatus.Success));
        Assert.Equal(requests - limit, results.Count(r => r.Status == ExplainTripResultStatus.TripLimitReached));
        Assert.Equal(limit, await seed.LlmCallLogs.CountAsync(l => l.Outcome == LlmCallOutcome.Succeeded));
        Assert.Equal(limit, await seed.AiUsageAdmissions.CountAsync(a => a.State == AiUsageAdmissionState.Completed));
    }

    [Theory]
    [InlineData(LlmCallOutcome.ProviderFailed, false)]
    [InlineData(LlmCallOutcome.InvalidOutput, false)]
    [InlineData(LlmCallOutcome.ProviderFailed, true)]
    [InlineData(LlmCallOutcome.InvalidOutput, true)]
    public async Task C05_C06_C07_FailedProviderOrInvalidOutput_DailyCharged_ExplainHoldReleased(
        LlmCallOutcome outcome, bool explain)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var trip = await Trip(c, user.Id);
        var place = await c.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.PlaceId).SingleAsync();
        var llm = new FakeLlm((_, _) => outcome == LlmCallOutcome.ProviderFailed
            ? Task.FromException<LlmJsonResponse>(new LlmUnavailableException("test provider failed"))
            : Task.FromResult(new LlmJsonResponse("not json", 7, 4)));
        if (explain) Assert.Equal(ExplainTripResultStatus.AiUnavailable, (await Explainer(c, llm).ExplainAsync(user.Id, trip.Id)).Status);
        else Assert.Equal(ParseTripRequestResultStatus.AiUnavailable, (await Parser(c, llm).ParseAsync(user.Id, ParseRequest)).Status);
        var completed = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        var log = await c.LlmCallLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.Completed, completed.State);
        Assert.Equal(outcome, completed.Outcome);
        Assert.Equal(log.Id, completed.LlmCallLogId);
        Assert.Equal(outcome, log.Outcome);
        Assert.Equal("fake", log.Model);
        Assert.Null(completed.RecoveryAfter);
        if (explain)
        {
            Assert.Null(await c.Trips.Where(t => t.Id == trip.Id).Select(t => t.AiExplainedAt).SingleAsync());
            var later = new FakeLlm((_, _) => Task.FromResult(ExplainAnswer(place)));
            Assert.Equal(ExplainTripResultStatus.Success, (await Explainer(c, later).ExplainAsync(user.Id, trip.Id)).Status);
            Assert.Equal(1, later.Calls);
        }
        // Original failure still occupies a daily slot; never refunded by completion.
        var repository = Repository(c);
        for (var i = explain ? 2 : 1; i < 3; i++)
            Assert.Equal(AiUsageStatus.Allowed, (await repository.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await repository.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task C08_C09_PreDispatchCancellationOrPreparationFailure_ReleasesWithoutLog(bool cancel)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        using var caller = new CancellationTokenSource();
        var llm = new FakeLlm();
        var tags = new PreparationTags(() =>
        {
            if (cancel) { caller.Cancel(); caller.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("test preparation failed");
        });
        var service = Parser(c, llm, tags: tags);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ParseAsync(user.Id, ParseRequest, caller.Token));
        else Assert.Equal("test preparation failed", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ParseAsync(user.Id, ParseRequest))).Message);
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.Released, row.State);
        Assert.Equal(2, row.FencingGeneration);
        Assert.Null(row.DispatchAuthorizedAt);
        Assert.Equal(0, llm.Calls);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
        Assert.Equal(ParseTripRequestResultStatus.Success, (await Parser(c, llm).ParseAsync(user.Id, ParseRequest)).Status);
    }

    [Fact]
    public async Task C10_NoDescribableStops_ChecksQuotaThenReleasesAndAppliesFixedReasons()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var trip = await Trip(c, user.Id, description: null);
        var llm = new FakeLlm();
        var result = await Explainer(c, llm).ExplainAsync(user.Id, trip.Id);
        Assert.Equal(ExplainTripResultStatus.Success, result.Status);
        Assert.Equal(TripExplanationPrompt.InsufficientDataReason, Assert.Single(result.Response!.Items).Reasoning);
        Assert.Equal(0, llm.Calls);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
        Assert.Equal(AiUsageAdmissionState.Released, (await c.AiUsageAdmissions.AsNoTracking().SingleAsync()).State);
        for (var i = 0; i < 3; i++) await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(ExplainTripResultStatus.DailyLimitReached, (await Explainer(c, llm).ExplainAsync(user.Id, trip.Id)).Status);
        Assert.Equal(4, await c.AiUsageAdmissions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C11_PostDispatchCancellation_RetainsChargeAndTripHold_WithoutFakeLog(bool explain)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var trip = await Trip(c, user.Id);
        using var caller = new CancellationTokenSource();
        var llm = new FakeLlm((_, ct) => { caller.Cancel(); return Task.FromCanceled<LlmJsonResponse>(ct); });
        if (explain) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Explainer(c, llm).ExplainAsync(user.Id, trip.Id, caller.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Parser(c, llm).ParseAsync(user.Id, ParseRequest, caller.Token));
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.DispatchAuthorized, row.State);
        Assert.Equal(row.DispatchAuthorizedAt!.Value.AddMinutes(2), row.RecoveryAfter);
        Assert.Null(row.Outcome);
        Assert.Null(row.LlmCallLogId);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
        if (explain) Assert.Equal(AiUsageStatus.TripLimitReached,
            (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.Explain, trip.Id)).Decision.Status);
        for (var i = 1; i < 3; i++) await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(AiUsageStatus.DailyLimitReached, (await Repository(c).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C12_C14_CompletionRetry_ExactlyOneRealLog_NoProviderResend_IdentityAndResultFenced(bool explain)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var trip = await Trip(c, user.Id);
        var place = await c.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.PlaceId).SingleAsync();
        var llm = new FakeLlm((_, _) => Task.FromResult(explain ? ExplainAnswer(place) : ParseAnswer));
        if (explain) Assert.Equal(ExplainTripResultStatus.Success, (await Explainer(c, llm).ExplainAsync(user.Id, trip.Id)).Status);
        else Assert.Equal(ParseTripRequestResultStatus.Success, (await Parser(c, llm).ParseAsync(user.Id, ParseRequest)).Status);
        var appliedAt = await c.Trips.Where(t => t.Id == trip.Id).Select(t => t.AiExplainedAt).SingleAsync();
        var item = await c.ItineraryItems.AsNoTracking().SingleAsync(i => i.TripId == trip.Id);
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        var log = await c.LlmCallLogs.AsNoTracking().SingleAsync();
        var handle = Handle(row);
        var completion = new AiUsageCompletion(log.Outcome, log.Model, log.InputTokens, log.OutputTokens, log.DurationMilliseconds);
        await using var second = db.Context();
        var retry = await Repository(second).CompleteAsync(handle, completion);
        Assert.False(retry.CompletedNow);
        Assert.Equal(log.Id, retry.LlmCallLogId);
        Assert.Equal(1, llm.Calls);
        Assert.Equal(1, await c.LlmCallLogs.CountAsync());
        Assert.Equal(appliedAt, await c.Trips.Where(t => t.Id == trip.Id).Select(t => t.AiExplainedAt).SingleAsync());
        var unchanged = await c.ItineraryItems.AsNoTracking().SingleAsync(i => i.TripId == trip.Id);
        Assert.Equal(item.Reasoning, unchanged.Reasoning);
        Assert.Equal(item.UpdatedAt, unchanged.UpdatedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(c).CompleteAsync(handle with { FencingToken = Guid.NewGuid() }, completion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(c).CompleteAsync(handle with
            { Kind = explain ? LlmCallKind.ParseRequest : LlmCallKind.Explain }, completion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(c).CompleteAsync(handle with { FencingGeneration = 2 }, completion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(c).CompleteAsync(handle with { TripIdSnapshot = Guid.NewGuid() }, completion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(c).CompleteAsync(handle, completion with { Outcome = LlmCallOutcome.ProviderFailed }));
    }

    [Fact]
    public async Task ConcurrentCompletionOnIndependentConnections_CreatesOneLogAndOneTerminalState()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var repo = Repository(c);
        var row = (await repo.AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Admission!;
        Assert.True(await repo.AuthorizeDispatchAsync(user.Id, row.Id, row.FencingToken, row.FencingGeneration));
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var other = db.Context();
            await barrier.Task;
            return await Repository(other).CompleteAsync(Handle(row), new(LlmCallOutcome.Succeeded, "fake", 7, 4, 0));
        }).ToArray();
        barrier.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(Deadline);
        Assert.Single(results, r => r.CompletedNow);
        Assert.Equal(results[0].LlmCallLogId, results[1].LlmCallLogId);
        Assert.Equal(1, await c.LlmCallLogs.CountAsync());
        Assert.Equal(AiUsageAdmissionState.Completed, (await c.AiUsageAdmissions.AsNoTracking().SingleAsync()).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C13_C14_LogInsertThenUpdateFailure_RollsBackBoth_DBOnlyRetryDoesNotResend(bool explain)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        var fault = new CompletionFault();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(fault).Options);
        var user = await User(c);
        var trip = await Trip(c, user.Id);
        var place = await c.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.PlaceId).SingleAsync();
        var llm = new FakeLlm((_, _) => Task.FromResult(explain ? ExplainAnswer(place) : ParseAnswer));
        if (explain) await Assert.ThrowsAsync<InvalidOperationException>(() => Explainer(c, llm).ExplainAsync(user.Id, trip.Id));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Parser(c, llm).ParseAsync(user.Id, ParseRequest));
        Assert.True(fault.SawLogInsert);
        Assert.Equal(1, llm.Calls);
        Assert.Empty(await c.LlmCallLogs.AsNoTracking().ToListAsync());
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.DispatchAuthorized, row.State);
        Assert.Null(row.LlmCallLogId);
        Assert.Null(row.CompletedAt);
        Assert.Null(await c.Trips.Where(t => t.Id == trip.Id).Select(t => t.AiExplainedAt).SingleAsync());
        fault.Enabled = false;
        var result = await Repository(c).CompleteAsync(Handle(row), new(LlmCallOutcome.Succeeded, "fake", 7, 4, 0));
        Assert.True(result.CompletedNow);
        Assert.Equal(1, await c.LlmCallLogs.CountAsync());
        Assert.Equal(1, llm.Calls);
        // Retrying completion is DB-only: it cannot apply reasoning or fabricate service success.
        var retry = await Repository(c).CompleteAsync(Handle(row), new(LlmCallOutcome.Succeeded, "fake", 7, 4, 0));
        Assert.False(retry.CompletedNow);
        Assert.Null(await c.Trips.Where(t => t.Id == trip.Id).Select(t => t.AiExplainedAt).SingleAsync());
        Assert.Equal("old reason", await c.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.Reasoning).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C15_SuccessThenTripFinalizedOrDeleted_CompletionPersists_ApplyReturnsExistingStatus(bool deleted)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var trip = await Trip(c, user.Id);
        var place = await c.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.PlaceId).SingleAsync();
        var llm = new FakeLlm(async (_, _) =>
        {
            await using var other = db.Context();
            if (deleted) await other.Trips.Where(t => t.Id == trip.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, Now));
            else await other.Trips.Where(t => t.Id == trip.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TripStatus.Finalized));
            return ExplainAnswer(place);
        });
        Assert.Equal(ExplainTripResultStatus.TripFinalized, (await Explainer(c, llm).ExplainAsync(user.Id, trip.Id)).Status);
        Assert.Equal(LlmCallOutcome.Succeeded, (await c.LlmCallLogs.AsNoTracking().SingleAsync()).Outcome);
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.Completed, row.State);
        Assert.Equal(LlmCallOutcome.Succeeded, row.Outcome);
        Assert.Null(await c.Trips.Where(t => t.Id == trip.Id).Select(t => t.AiExplainedAt).SingleAsync());
        Assert.Equal("old reason", await c.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.Reasoning).SingleAsync());
    }

    [Fact]
    public async Task C16_ServiceCrossesVietnamMidnight_AdmissionDayRetained_LinkedLogNotCountedNextDay()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var before = new DateTime(2026, 10, 6, 16, 59, 59, DateTimeKind.Utc);
        var clock = new Clock(before);
        var llm = new FakeLlm((_, _) => { clock.Now = before.AddSeconds(2); return Task.FromResult(ParseAnswer); });
        await Parser(c, llm, clock).ParseAsync(user.Id, ParseRequest);
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 6), row.VietnamUsageDate);
        Assert.Equal(before, row.AdmittedAt);
        Assert.Equal(clock.Now, row.CompletedAt);
        // BaseEntity uses real wall time. Place the real linked log at the simulated completion instant.
        await c.LlmCallLogs.Where(l => l.Id == row.LlmCallLogId).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, clock.Now));
        for (var i = 0; i < 3; i++) Assert.Equal(AiUsageStatus.Allowed,
            (await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
        Assert.Equal(AiUsageStatus.DailyLimitReached,
            (await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest)).Decision.Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task C18_C19_DisabledOrUnconfigured_NoAdmissionOrProvider(bool disabled, bool unconfigured)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        var probe = new ResolutionProbe();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(probe).Options);
        var user = await User(c);
        var trip = await Trip(c, user.Id);
        var llm = new FakeLlm { IsConfigured = !unconfigured };
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.AiEnabled, disabled ? 0 : 1);
        // Corrupt resolution cannot take precedence over availability gates.
        Assert.Equal(ParseTripRequestResultStatus.AiUnavailable, (await Parser(c, llm, settings: settings).ParseAsync(user.Id, ParseRequest)).Status);
        Assert.Equal(ExplainTripResultStatus.AiUnavailable, (await Explainer(c, llm, settings: settings).ExplainAsync(user.Id, trip.Id)).Status);
        Assert.Equal(0, probe.Reads);
        if (unconfigured) Assert.DoesNotContain(SystemSettingKeys.AiEnabled, settings.RequestedKeys);
        Assert.Equal(0, llm.Calls);
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
    }

    [Theory]
    [InlineData(0, 1, ExplainTripResultStatus.DailyLimitReached)]
    [InlineData(3, 0, ExplainTripResultStatus.TripLimitReached)]
    [InlineData(0, 0, ExplainTripResultStatus.TripLimitReached)]
    public async Task C20_ZeroTerms_NeverCallProvider_TripPrecedesDaily(int daily, int explain, ExplainTripResultStatus expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        await PublishFree(c, daily, explain);
        var trip = await Trip(c, user.Id);
        var llm = new FakeLlm();
        Assert.Equal(expected, (await Explainer(c, llm).ExplainAsync(user.Id, trip.Id)).Status);
        if (daily == 0) Assert.Equal(ParseTripRequestResultStatus.DailyLimitReached,
            (await Parser(c, llm).ParseAsync(user.Id, ParseRequest)).Status);
        Assert.Equal(0, llm.Calls);
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
    }

    [Fact]
    public async Task AuthorizationExpired_NeverInvokesProvider_AndStaleOwnerCannotComplete()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c);
        var clock = new Clock(Now);
        var llm = new FakeLlm();
        var tags = new PreparationTags(() => clock.Now = Now.AddSeconds(31));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Parser(c, llm, clock, tags).ParseAsync(user.Id, ParseRequest));
        Assert.Equal(0, llm.Calls);
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.Released, row.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository(c, clock).CompleteAsync(Handle(row), new(LlmCallOutcome.Succeeded, "fake", 7, 4, 0)));
    }

    [Fact]
    public async Task ExpiryDuringProvider_DoesNotRevokeAdmission_PreservesExactPlanSnapshot()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, PlanCode.Membership);
        var trip = await Trip(c, user.Id);
        var place = await c.ItineraryItems.Where(i => i.TripId == trip.Id).Select(i => i.PlaceId).SingleAsync();
        var clock = new Clock(Now);
        var llm = new FakeLlm((_, _) => { clock.Now = Now.AddDays(7); return Task.FromResult(ExplainAnswer(place)); });
        Assert.Equal(ExplainTripResultStatus.Success, (await Explainer(c, llm, clock).ExplainAsync(user.Id, trip.Id)).Status);
        var row = await c.AiUsageAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(AiUsageAdmissionState.Completed, row.State);
        Assert.Equal(SubscriptionBaseline.VersionId(PlanCode.Membership), row.ResolvedPlanVersionId);
        Assert.Equal(30, row.AdmittedDailyLimit);
        Assert.Equal(3, row.AdmittedExplainLimit);
        Assert.Equal(ExplainTripResultStatus.TripLimitReached, (await Explainer(c, llm, clock).ExplainAsync(user.Id, trip.Id)).Status);
        Assert.Equal(1, llm.Calls);
        var next = await Repository(c, clock).AdmitAsync(Guid.NewGuid(), user.Id, LlmCallKind.ParseRequest);
        Assert.Equal(SubscriptionBaseline.VersionId(PlanCode.Free), next.Admission!.ResolvedPlanVersionId);
        Assert.Equal(3, next.Admission.AdmittedDailyLimit);
    }

    [Fact]
    public async Task EnabledResolverFailure_NeverAwardsFallbackAdmission()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        var probe = new ResolutionProbe();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(probe).Options);
        var user = await User(c);
        var llm = new FakeLlm();
        Assert.Equal("test resolver failure", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Parser(c, llm).ParseAsync(user.Id, ParseRequest))).Message);
        Assert.Equal(1, probe.Reads);
        Assert.Equal(0, llm.Calls);
        Assert.Empty(await c.AiUsageAdmissions.ToListAsync());
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
    }

    [Fact]
    public async Task ReleaseInfrastructureFailure_DoesNotHideOriginalPreparationException()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        var fault = new ReleaseFault();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, o => o.UseNetTopologySuite()).AddInterceptors(fault).Options);
        var user = await User(c);
        var llm = new FakeLlm();
        var tags = new PreparationTags(() => { fault.Enabled = true; throw new InvalidOperationException("original preparation error"); });
        Assert.Equal("original preparation error", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Parser(c, llm, tags: tags).ParseAsync(user.Id, ParseRequest))).Message);
        Assert.True(fault.Fired);
        Assert.Equal(0, llm.Calls);
        Assert.Equal(AiUsageAdmissionState.Reserved, (await c.AiUsageAdmissions.AsNoTracking().SingleAsync()).State);
        Assert.Empty(await c.LlmCallLogs.ToListAsync());
    }

    private static AiUsageAdmissionRepository Repository(AppDbContext c, Clock? clock = null, FakeSystemSettingProvider? settings = null) =>
        new(c, settings ?? new(), clock ?? new(Now));

    private static IAiUsageCoordinator Coordinator(AppDbContext c, ILlmClient llm, Clock? clock = null, FakeSystemSettingProvider? settings = null)
    {
        settings ??= new();
        return new AiUsageCoordinator(Repository(c, clock, settings), llm, settings, NullLogger<AiUsageCoordinator>.Instance);
    }

    private static TripRequestParsingService Parser(AppDbContext c, ILlmClient llm, Clock? clock = null,
        ITagRepository? tags = null, FakeSystemSettingProvider? settings = null) => new(new UserRepository(c),
            tags ?? new TagRepository(c), new MasterData(), Coordinator(c, llm, clock, settings), llm, clock ?? new(Now));

    private static TripExplanationService Explainer(AppDbContext c, ILlmClient llm, Clock? clock = null,
        FakeSystemSettingProvider? settings = null) => new(new UserRepository(c), new TripExplanationRepository(c),
            Coordinator(c, llm, clock, settings), llm, clock ?? new(Now));

    private static AiUsageHandle Handle(AiUsageAdmission a) => new(a.Id, a.UserId, a.Kind, a.TripIdSnapshot, a.FencingToken, a.FencingGeneration);
    private static LlmJsonResponse ParseAnswer => new("{\"isTripRequest\":true,\"durationHours\":3}", 7, 4);
    private static LlmJsonResponse ExplainAnswer(Guid place) => new(JsonSerializer.Serialize(new
        { stops = new[] { new { placeId = place, hasEnoughData = true, reason = "Verified database facts." } } }), 7, 4);

    private static async Task<User> User(AppDbContext c, PlanCode plan = PlanCode.Free)
    {
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        if (plan != PlanCode.Free)
        {
            var version = await c.SubscriptionPlanVersions.AsNoTracking().SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(plan));
            var legacy = new UserSubscription { UserId = user.Id, PlanCode = plan, StartsAt = Now.AddDays(-1), EndsAt = Now.AddDays(6) };
            c.UserSubscriptions.Add(legacy);
            await c.SaveChangesAsync();
            c.SubscriptionPeriods.Add(new SubscriptionPeriod { UserId = user.Id, PlanId = version.PlanId, PlanVersionId = version.Id,
                LegacyUserSubscriptionId = legacy.Id, StartsAt = legacy.StartsAt, EndsAt = legacy.EndsAt });
            await c.SaveChangesAsync();
        }
        return user;
    }

    private static async Task PublishFree(AppDbContext c, int daily, int explain)
    {
        var version = new SubscriptionPlanVersion { PlanId = SubscriptionBaseline.PlanId(PlanCode.Free), VersionNumber = 2,
            Price = 0, GenerateLimit = 3, SavedTripLimit = 1, Origin = PlanVersionOrigin.Published, PublishedAt = Now,
            AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain };
        await new SubscriptionRepository(c).PublishVersionAsync(version);
    }

    private static async Task<Trip> Trip(AppDbContext c, Guid userId, string? description = "Known local coffee place")
    {
        var trip = await PostgresTestDatabase.InsertTripAsync(c, userId);
        var place = new Place { Name = "Test cafe", Address = "Local", Description = description,
            Location = new Point(106.5, 10.75) { SRID = 4326 }, Category = PlaceCategory.Cafe, Status = PlaceStatus.Active };
        c.ItineraryItems.Add(new ItineraryItem { TripId = trip.Id, Place = place, OrderIndex = 0,
            ScheduledTime = new TimeOnly(10, 0), EstimatedDurationMinutes = 60, Reasoning = "old reason" });
        await c.SaveChangesAsync();
        return trip;
    }

    // Wait for the denied requests only; provider requests remain paused on an explicit barrier.
    private static async Task WaitForCompleted<T>(Task<T>[] tasks, int count)
    {
        var remaining = tasks.ToList();
        for (var i = 0; i < count; i++)
        {
            var done = await Task.WhenAny(remaining).WaitAsync(Deadline);
            await done;
            remaining.Remove(done);
        }
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeLlm(Func<LlmJsonRequest, CancellationToken, Task<LlmJsonResponse>>? answer = null) : ILlmClient
    {
        private int calls;
        public int Calls => calls;
        public string Model => "fake";
        public bool IsConfigured { get; set; } = true;
        public Task<LlmJsonResponse> GenerateJsonAsync(LlmJsonRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            return answer?.Invoke(request, cancellationToken) ?? Task.FromResult(ParseAnswer);
        }
    }

    private sealed class ProviderBarrier(int expected)
    {
        private int calls;
        public int Calls => calls;
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task EnterAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) == expected) Arrived.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class PreparationTags(Action prepare) : ITagRepository
    {
        public Task<IReadOnlyList<Tag>> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            prepare();
            return Task.FromResult<IReadOnlyList<Tag>>([]);
        }
        public Task<IReadOnlyList<Tag>> GetByIdsAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MasterData : IMasterDataService
    {
        public Task<MasterDataResponse> GetMasterDataAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MasterDataResponse([], [], [], [], [], new TripLimitsResponse(1, 24), []));
    }

    private sealed class ReleaseFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("FROM \"Users\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                Fired = true;
                throw new InvalidOperationException("test release infrastructure failure");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ResolutionProbe : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"SubscriptionPeriods\"", StringComparison.Ordinal))
            {
                Reads++;
                throw new InvalidOperationException("test resolver failure");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CompletionFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; } = true;
        public bool SawLogInsert { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"LlmCallLogs\"", StringComparison.Ordinal)) SawLogInsert = true;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && SawLogInsert && command.CommandText.Contains("UPDATE \"AiUsageAdmissions\"", StringComparison.Ordinal))
                throw new InvalidOperationException("test completion update failure");
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
