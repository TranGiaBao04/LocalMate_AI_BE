using System.Data.Common;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LocalMateAI.Tests;

public sealed class AdminAiUsageRepositoryPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 10, 13);
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    [Theory]
    [InlineData(AiUsageAdmissionState.Released, null, null)]
    [InlineData(AiUsageAdmissionState.Reserved, null, "InProgress")]
    [InlineData(AiUsageAdmissionState.DispatchAuthorized, null, "InProgress")]
    [InlineData(AiUsageAdmissionState.Completed, LlmCallOutcome.Succeeded, "Succeeded")]
    [InlineData(AiUsageAdmissionState.Completed, LlmCallOutcome.ProviderFailed, "ProviderFailed")]
    [InlineData(AiUsageAdmissionState.Completed, LlmCallOutcome.InvalidOutput, "InvalidOutput")]
    [InlineData(AiUsageAdmissionState.Abandoned, null, "Abandoned")]
    public async Task R01_R06_R10_AdmissionStatesAndExactEvidence(AiUsageAdmissionState state,
        LlmCallOutcome? outcome, string? status)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        await Admission(c, user.Id, 10, state, Now, outcome);
        var repo = new AdminAiUsageRepository(c);
        var page = await repo.GetUserCallsAsync(user.Id, new());
        if (status is null) { Assert.Empty(page.Items); Assert.Equal(0, page.TotalCount); return; }
        var row = Assert.Single(page.Items);
        Assert.Equal(Id(10), row.Id);
        Assert.Equal(status, row.Status);
        Assert.Equal(Now, row.StartedAt);
        Assert.Equal(state is AiUsageAdmissionState.Completed or AiUsageAdmissionState.Abandoned ? Now.AddSeconds(2) : null,
            row.CompletedAt);
        Assert.Equal(state == AiUsageAdmissionState.Completed ? 11 : 0, row.InputTokens);
        Assert.Equal(state == AiUsageAdmissionState.Completed ? 7 : 0, row.OutputTokens);
        Assert.Equal(state == AiUsageAdmissionState.Completed ? 123 : null, row.DurationMs);
        Assert.Equal(state == AiUsageAdmissionState.Completed ? "fixed-provider-model" : null, row.Model);
        Assert.Equal(1, (await repo.GetUserSummaryAsync(user.Id, Day, Day)).Totals.Calls);
    }

    [Theory]
    [InlineData(LlmCallOutcome.Succeeded)]
    [InlineData(LlmCallOutcome.ProviderFailed)]
    [InlineData(LlmCallOutcome.InvalidOutput)]
    public async Task R07_LegacyOutcomeAndTimestamp(LlmCallOutcome outcome)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        await Log(c, user.Id, 20, Now, outcome);
        var row = Assert.Single((await new AdminAiUsageRepository(c).GetUserCallsAsync(user.Id, new())).Items);
        Assert.Equal(Id(20), row.Id);
        Assert.Equal(outcome.ToString(), row.Status);
        Assert.Equal(Now, row.CompletedAt);
        Assert.Equal("fixed-provider-model", row.Model);
    }

    [Fact]
    public async Task R08_AntiJoinIsGlobal_NotRestrictedByDateKindOrUser()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        var other = await User(c, 2);
        var admission = await Admission(c, other.Id, 10, AiUsageAdmissionState.Completed,
            Now.AddDays(-1), LlmCallOutcome.Succeeded);
        // The FK permits mismatched historical evidence; anti-join must still exclude it globally.
        await c.LlmCallLogs.Where(log => log.Id == admission.LlmCallLogId)
            .ExecuteUpdateAsync(set => set.SetProperty(log => log.UserId, user.Id)
                .SetProperty(log => log.Kind, LlmCallKind.Explain).SetProperty(log => log.CreatedAt, Now)
                .SetProperty(log => log.Outcome, LlmCallOutcome.ProviderFailed));
        var result = await new AdminAiUsageRepository(c).GetUserCallsAsync(user.Id,
            new() { From = Day, To = Day, Kind = "Explain", Status = "ProviderFailed" });
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task R09_R16_SameGuidAcrossSources_StableIdAndSourcePaging()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        await Admission(c, user.Id, 10, AiUsageAdmissionState.Reserved, Now);
        await Log(c, user.Id, 10, Now);
        await Log(c, user.Id, 9, Now);
        var repo = new AdminAiUsageRepository(c);
        foreach (var direction in new[] { "asc", "desc" })
        {
            var pages = new List<AdminAiCallReadModel>();
            for (var page = 1; page <= 3; page++)
                pages.Add(Assert.Single((await repo.GetUserCallsAsync(user.Id,
                    new() { Page = page, PageSize = 1, SortDirection = direction })).Items));
            Assert.Equal(new[] { Id(9), Id(10), Id(10) }, pages.Select(row => row.Id));
            Assert.Equal(new[] { "Succeeded", "InProgress", "Succeeded" }, pages.Select(row => row.Status));
        }
        Assert.Equal(3, (await repo.GetSystemSummaryAsync(Day, Day)).Totals.Calls);
    }

    [Fact]
    public async Task R11_VietnamBoundaryUsesAdmissionDateAndLegacyUtcRange()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        var boundary = new DateTime(2026, 10, 12, 17, 0, 0, DateTimeKind.Utc);
        await Log(c, user.Id, 10, boundary.AddMicroseconds(-1));
        await Log(c, user.Id, 11, boundary);
        await Admission(c, user.Id, 12, AiUsageAdmissionState.Reserved, boundary.AddSeconds(-1));
        await Admission(c, user.Id, 13, AiUsageAdmissionState.Reserved, boundary);
        var repo = new AdminAiUsageRepository(c);
        var page = await repo.GetUserCallsAsync(user.Id, new() { From = Day, To = Day });
        Assert.Equal(new[] { Id(11), Id(13) }, page.Items.Select(row => row.Id).OrderBy(id => id));
        var summary = await repo.GetUserSummaryAsync(user.Id, Day.AddDays(-1), Day);
        Assert.Equal(new long[] { 2, 2 }, summary.Days.Select(day => day.Calls));
        Assert.Equal(new[] { Day.AddDays(-1), Day }, summary.Days.Select(day => day.Date));
    }

    [Fact]
    public async Task R12_LockedUserExistsAndIsIncluded_NoTracking()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        user.Status = UserStatus.Locked;
        await c.SaveChangesAsync();
        await Log(c, user.Id, 10, Now);
        c.ChangeTracker.Clear();
        var repo = new AdminAiUsageRepository(c);
        Assert.True(await repo.UserExistsAsync(user.Id));
        Assert.False(await repo.UserExistsAsync(Id(99)));
        var summary = await repo.GetSystemSummaryAsync(Day, Day);
        Assert.Equal(user.Id, Assert.Single(summary.TopUsers).UserId);
        Assert.Equal(1, summary.Totals.ActiveUsers);
        Assert.Empty(c.ChangeTracker.Entries());
    }

    [Fact]
    public async Task R13_DeletedTripNullsLiveIdButRetainsHistoryAndSnapshot()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        await Admission(c, user.Id, 10, AiUsageAdmissionState.Completed, Now, LlmCallOutcome.Succeeded, trip.Id);
        await Log(c, user.Id, 20, Now, tripId: trip.Id);
        await c.Trips.Where(t => t.Id == trip.Id).ExecuteDeleteAsync();
        var rows = (await new AdminAiUsageRepository(c).GetUserCallsAsync(user.Id, new())).Items;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Null(row.TripId));
        Assert.Equal(trip.Id, (await c.AiUsageAdmissions.AsNoTracking().SingleAsync()).TripIdSnapshot);
    }

    [Theory]
    [InlineData(null, null, 3)]
    [InlineData("2026-10-13", null, 2)]
    [InlineData(null, "2026-10-13", 2)]
    [InlineData("2026-10-13", "2026-10-13", 1)]
    [InlineData("0001-01-01", "9999-12-31", 3)]
    [InlineData("0001-01-01", "2026-10-13", 2)]
    [InlineData("2026-10-13", "9999-12-31", 2)]
    public async Task R14_OptionalAndExtremeBounds(string? from, string? to, int expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        for (var i = 0; i < 3; i++) await Log(c, user.Id, 10 + i, Now.AddDays(i - 1));
        var page = await new AdminAiUsageRepository(c).GetUserCallsAsync(user.Id,
            new() { From = from is null ? null : DateOnly.Parse(from), To = to is null ? null : DateOnly.Parse(to) });
        Assert.Equal(expected, page.TotalCount);
    }

    [Fact]
    public async Task R15_R16_R17_FiltersBeforeCountAndSqlPaging_SearchIgnored()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var seed = db.Context();
        var user = await User(seed, 1);
        await Log(seed, user.Id, 10, Now, LlmCallOutcome.ProviderFailed, kind: LlmCallKind.Explain);
        await Log(seed, user.Id, 11, Now.AddHours(1), LlmCallOutcome.ProviderFailed, kind: LlmCallKind.Explain);
        await Log(seed, user.Id, 12, Now, LlmCallOutcome.Succeeded);
        var trace = new SqlTrace();
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, options => options.UseNetTopologySuite()).AddInterceptors(trace).Options);
        var repo = new AdminAiUsageRepository(c);
        var query = new AdminAiCallsQuery { Kind = "Explain", Status = "ProviderFailed", PageSize = 1,
            Search = new string('x', 200) };
        var page = await repo.GetUserCallsAsync(user.Id, query);
        Assert.Equal(Id(11), Assert.Single(page.Items).Id);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(Id(10), Assert.Single((await repo.GetUserCallsAsync(user.Id, query with { SortDirection = "asc" })).Items).Id);
        var beyond = await repo.GetUserCallsAsync(user.Id, query with { Page = 10000 });
        Assert.Empty(beyond.Items);
        Assert.Equal(2, beyond.TotalCount);
        Assert.Equal(2, beyond.TotalPages);
        Assert.Contains(trace.Commands, sql => sql.Contains("LIMIT") && sql.Contains("OFFSET") && sql.Contains("ORDER BY"));
        Assert.Contains(trace.Commands, sql => sql.Contains("count(*)") && sql.Contains("\"Status\" ="));
    }

    [Fact]
    public async Task R18_R20_DailyParityAndGlobalAggregates()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        var other = await User(c, 2);
        await Admission(c, user.Id, 10, AiUsageAdmissionState.Completed, Now, LlmCallOutcome.Succeeded);
        await Admission(c, user.Id, 11, AiUsageAdmissionState.Completed, Now, LlmCallOutcome.ProviderFailed);
        await Admission(c, user.Id, 12, AiUsageAdmissionState.Abandoned, Now);
        await Admission(c, user.Id, 13, AiUsageAdmissionState.Reserved, Now);
        await Admission(c, user.Id, 14, AiUsageAdmissionState.DispatchAuthorized, Now);
        await Admission(c, user.Id, 15, AiUsageAdmissionState.Released, Now);
        await Log(c, user.Id, 20, Now, LlmCallOutcome.InvalidOutput, kind: LlmCallKind.Explain);
        await Log(c, other.Id, 21, Now, kind: LlmCallKind.Explain);
        var repo = new AdminAiUsageRepository(c);
        var personal = await repo.GetUserSummaryAsync(user.Id, Day, Day);
        var count = await new AiUsageAdmissionRepository(c, new FakeSystemSettingProvider(),
            new PlanVersionFoundationPostgresTests.Clock(Now)).CountDailyAsync(user.Id, Day);
        Assert.Equal(count, personal.Totals.Calls);
        Assert.Equal(count, (await repo.GetUserCallsAsync(user.Id, new() { From = Day, To = Day })).TotalCount);
        var global = await repo.GetSystemSummaryAsync(Day, Day);
        Assert.Equal(7, global.Totals.Calls);
        Assert.Equal(2, global.Totals.Succeeded);
        Assert.Equal(1, global.Totals.ProviderFailed);
        Assert.Equal(1, global.Totals.InvalidOutput);
        Assert.Equal(1, global.Totals.Abandoned);
        Assert.Equal(2, global.Totals.InProgress);
        Assert.Equal(44, global.Totals.InputTokens);
        Assert.Equal(28, global.Totals.OutputTokens);
        Assert.Equal(492, global.Totals.DurationSumMs);
        Assert.Equal(4, global.Totals.DurationCount);
        Assert.Equal(2, global.Totals.ActiveUsers);
        Assert.Equal(3, Assert.Single(global.Days).Failed);
        Assert.Equal(global.Totals.Calls, global.Days.Sum(day => day.Calls));
        Assert.Equal(global.Totals.Calls, global.ByKind.Sum(kind => kind.Calls));
        Assert.Equal(6, global.TopUsers[0].Calls);
        Assert.Empty(personal.TopUsers);
    }

    [Fact]
    public async Task R19_EmptyAggregatesAndPages()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        var repo = new AdminAiUsageRepository(c);
        var summary = await repo.GetSystemSummaryAsync(Day, Day);
        Assert.Equal(AdminAiUsageTotals.Empty, summary.Totals);
        Assert.Empty(summary.Days);
        Assert.Empty(summary.ByKind);
        Assert.Empty(summary.TopUsers);
        var page = await repo.GetUserCallsAsync(user.Id, new());
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public async Task R20_TopTenAreBoundedAndTiesUseUserId_ActiveUsersDistinctAcrossDays()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        for (var i = 1; i <= 12; i++)
        {
            var user = await User(c, i);
            await Log(c, user.Id, 100 + i, Now);
            await Log(c, user.Id, 200 + i, Now.AddDays(-1));
        }
        var summary = await new AdminAiUsageRepository(c).GetSystemSummaryAsync(Day.AddDays(-1), Day);
        Assert.Equal(12, summary.Totals.ActiveUsers);
        Assert.Equal(10, summary.TopUsers.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(Id), summary.TopUsers.Select(user => user.UserId));
        Assert.All(summary.Days, day => Assert.Equal(12, day.ActiveUsers));
        Assert.All(summary.TopUsers, user => { Assert.Equal(2, user.Calls); Assert.Equal(22, user.InputTokens); });
    }

    [Fact]
    public async Task SummaryUsesOneReadOnlySnapshot_WhenAnotherConnectionWritesBetweenQueries()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var writer = db.Context();
        var user = await User(writer, 1);
        await Log(writer, user.Id, 10, Now);
        var trace = new SqlTrace { BeforeRead = async number =>
        {
            if (number == 2) await Log(writer, user.Id, 11, Now);
        }};
        await using var reader = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Connection, options => options.UseNetTopologySuite()).AddInterceptors(trace).Options);
        var summary = await new AdminAiUsageRepository(reader).GetSystemSummaryAsync(Day, Day);
        Assert.Equal(1, summary.Totals.Calls);
        Assert.Equal(1, Assert.Single(summary.Days).Calls);
        Assert.Equal(1, Assert.Single(summary.ByKind).Calls);
        Assert.Equal(1, Assert.Single(summary.TopUsers).Calls);
        Assert.Contains("SET TRANSACTION READ ONLY", trace.Commands);
        Assert.Equal(2, (await new AdminAiUsageRepository(writer).GetSystemSummaryAsync(Day, Day)).Totals.Calls);
    }

    [Fact]
    public async Task CompletedWithoutEvidenceIsRejectedByDatabase_NotInventedAsSuccess()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await User(c, 1);
        await Admission(c, user.Id, 10, AiUsageAdmissionState.Reserved, Now);
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "AiUsageAdmissions" SET "State"='Completed', "Outcome"='Succeeded',
                "DispatchAuthorizedAt"={Now}, "CompletedAt"={Now} WHERE "Id"={Id(10)}
            """));
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("InProgress", Assert.Single((await new AdminAiUsageRepository(c).GetUserCallsAsync(user.Id, new())).Items).Status);
    }

    private static async Task<User> User(AppDbContext c, int id)
    {
        var role = await c.Roles.FirstAsync(role => role.IsSystem);
        var user = new User { Id = Id(id), FullName = $"Test user {id}", Email = $"user{id}@example.invalid", RoleId = role.Id };
        c.Users.Add(user);
        await c.SaveChangesAsync();
        return user;
    }

    private static async Task Log(AppDbContext c, Guid userId, int id, DateTime at,
        LlmCallOutcome outcome = LlmCallOutcome.Succeeded, Guid? tripId = null, LlmCallKind kind = LlmCallKind.ParseRequest)
    {
        c.LlmCallLogs.Add(new LlmCallLog { Id = Id(id), UserId = userId, TripId = tripId, Kind = kind,
            Outcome = outcome, Model = "fixed-provider-model", InputTokens = 11, OutputTokens = 7, DurationMilliseconds = 123 });
        await c.SaveChangesAsync();
        await c.LlmCallLogs.Where(log => log.Id == Id(id)).ExecuteUpdateAsync(set => set.SetProperty(log => log.CreatedAt, at));
    }

    private static async Task<AiUsageAdmission> Admission(AppDbContext c, Guid userId, int id,
        AiUsageAdmissionState state, DateTime at, LlmCallOutcome? outcome = null, Guid? tripId = null)
    {
        if (state == AiUsageAdmissionState.Completed)
            await Log(c, userId, id + 1000, at.AddDays(1), outcome!.Value, tripId);
        var row = new AiUsageAdmission { Id = Id(id), UserId = userId, TripId = tripId, TripIdSnapshot = tripId,
            Kind = tripId.HasValue ? LlmCallKind.Explain : LlmCallKind.ParseRequest,
            State = state, Outcome = outcome, VietnamUsageDate = DateOnly.FromDateTime(at.AddHours(7)), AdmittedAt = at,
            ReservedUntil = at.AddSeconds(30), FencingToken = Id(id + 2000), FencingGeneration = 1,
            ResolvedPlanVersionId = SubscriptionBaseline.VersionId(PlanCode.Free), AdmittedDailyLimit = 3,
            AdmittedExplainLimit = tripId.HasValue ? 1 : null,
            DispatchAuthorizedAt = state is AiUsageAdmissionState.Completed or AiUsageAdmissionState.Abandoned
                or AiUsageAdmissionState.DispatchAuthorized ? at.AddSeconds(1) : null,
            CompletedAt = state is AiUsageAdmissionState.Completed or AiUsageAdmissionState.Abandoned
                or AiUsageAdmissionState.Released ? at.AddSeconds(2) : null,
            LlmCallLogId = state == AiUsageAdmissionState.Completed ? Id(id + 1000) : null };
        c.AiUsageAdmissions.Add(row);
        await c.SaveChangesAsync();
        return row;
    }

    private sealed class SqlTrace : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public Func<int, Task>? BeforeRead { get; init; }
        private int readCount;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            if (BeforeRead is not null) await BeforeRead(++readCount);
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
