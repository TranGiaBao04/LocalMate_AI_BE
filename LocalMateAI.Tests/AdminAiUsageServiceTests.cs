using System.Text.Json;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.AdminAiUsage;
using LocalMateAI.Application.Validators.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace LocalMateAI.Tests;

public sealed class AdminAiUsageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 13);
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task A01_A07_DefaultRangeHasThirtyZeroDaysAndBothKinds()
    {
        using var f = new Fixture();
        var result = await f.Service.GetSummaryAsync(new());
        Assert.Equal(AdminAiUsageResultStatus.Success, result.Status);
        var response = result.Response!;
        Assert.Equal(Day.AddDays(-29), response.From);
        Assert.Equal(Day, response.To);
        Assert.Equal((response.From, response.To), f.Repository.LastRange);
        Assert.Equal(30, response.Days.Count);
        Assert.Equal(Enumerable.Range(0, 30).Select(i => response.From.AddDays(i)), response.Days.Select(row => row.Date));
        Assert.All(response.Days, row => Assert.Equal(new AdminAiUsageSystemDayResponse(row.Date, 0, 0, 0, 0, 0, 0), row));
        Assert.Equal(new[] { "ParseRequest", "Explain" }, response.ByKind.Select(row => row.Kind));
        Assert.All(response.ByKind, row => Assert.Equal(0, row.Calls));
        Assert.Equal(0, response.Totals.Calls);
        Assert.Null(response.Totals.AverageDurationMs);
        Assert.Empty(response.TopUsers);
        Assert.Equal(Now.UtcDateTime, response.GeneratedAt);
        Assert.Equal(DateTimeKind.Utc, response.GeneratedAt.Kind);
    }

    [Fact]
    public async Task A02_A03_NormalizedRangesShareCacheAndRetainGeneratedAt()
    {
        using var f = new Fixture();
        var first = await f.Service.GetSummaryAsync(new());
        f.Clock.Now = Now.AddMinutes(1);
        var second = await f.Service.GetSummaryAsync(new() { From = " 2026-09-14 ", To = "2026-10-13" });
        Assert.Same(first.Response, second.Response);
        Assert.Equal(Now.UtcDateTime, second.Response!.GeneratedAt);
        Assert.Equal(1, f.Repository.SystemCalls);
    }

    [Fact]
    public async Task A04_CacheUsesAbsoluteFiveMinutes_NotSliding()
    {
        var clock = new MutableClock();
        using var cache = new ControlledCache(clock);
        using var f = new Fixture(clock, cache);
        var first = await f.Service.GetSummaryAsync(new());
        Assert.Equal(TimeSpan.FromMinutes(5), cache.LastTtl);
        clock.Now = Now.AddMinutes(4).AddSeconds(59);
        Assert.Same(first.Response, (await f.Service.GetSummaryAsync(new())).Response);
        clock.Now = Now.AddMinutes(5);
        var expired = await f.Service.GetSummaryAsync(new());
        Assert.NotSame(first.Response, expired.Response);
        Assert.Equal(clock.Now.UtcDateTime, expired.Response!.GeneratedAt);
        Assert.Equal(2, f.Repository.SystemCalls);
    }

    [Fact]
    public async Task A05_RepositoryFailurePropagatesAndDoesNotPopulateCache()
    {
        using var f = new Fixture();
        f.Repository.FailNext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.GetSummaryAsync(new()));
        Assert.Equal(0, f.Cache.Count);
        Assert.Equal(AdminAiUsageResultStatus.Success, (await f.Service.GetSummaryAsync(new())).Status);
        Assert.Equal(2, f.Repository.SystemCalls);
    }

    [Fact]
    public async Task A05_ResponseConstructionFailureIsNotCachedEither()
    {
        using var f = new Fixture();
        f.Repository.Data = f.Repository.Data with { ByKind = [new("Explain", 1, 1, 0, 0), new("Explain", 1, 1, 0, 0)] };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.GetSummaryAsync(new()));
        Assert.Equal(0, f.Cache.Count);
        f.Repository.Data = new(AdminAiUsageTotals.Empty, [], [], []);
        Assert.Equal(AdminAiUsageResultStatus.Success, (await f.Service.GetSummaryAsync(new())).Status);
        Assert.Equal(2, f.Repository.SystemCalls);
    }

    [Fact]
    public async Task A01_DefaultDayUsesVietnamDate_NotUtcDate()
    {
        using var f = new Fixture(new MutableClock { Now = new(2026, 10, 12, 17, 30, 0, TimeSpan.Zero) });
        var response = (await f.Service.GetSummaryAsync(new())).Response!;
        Assert.Equal(Day, response.To);
        Assert.Equal(Day.AddDays(-29), response.From);
    }

    [Fact]
    public async Task A02_DifferentResolvedDatesNeverShareCache()
    {
        using var f = new Fixture();
        await f.Service.GetSummaryAsync(new() { From = "2026-10-11", To = "2026-10-12" });
        await f.Service.GetSummaryAsync(new() { From = "2026-10-11", To = "2026-10-13" });
        await f.Service.GetSummaryAsync(new() { From = "2026-10-12", To = "2026-10-13" });
        Assert.Equal(3, f.Repository.SystemCalls);
    }

    [Theory]
    [InlineData("bad", null, "From")]
    [InlineData(null, "bad", "To")]
    [InlineData("2026-10-14", "2026-10-13", "From")]
    [InlineData(null, "2026-10-14", "To")]
    [InlineData("2025-01-01", "2026-10-13", "From")]
    public async Task A06_InvalidDatesHaveFieldErrorsBeforeRepositoryAndCache(string? from, string? to, string field)
    {
        using var f = new Fixture();
        var request = new DashboardDateRangeQuery { From = from, To = to };
        var a = await f.Service.GetSummaryAsync(request);
        var b = await f.Service.GetUserSummaryAsync(UserId, request);
        Assert.Equal(AdminAiUsageResultStatus.InvalidQuery, a.Status);
        Assert.Equal(AdminAiUsageResultStatus.InvalidQuery, b.Status);
        Assert.Contains(field, a.ValidationErrors!.Keys);
        Assert.Contains(field, b.ValidationErrors!.Keys);
        Assert.Null(a.Response);
        Assert.Equal(0, f.Repository.SystemCalls + f.Repository.UserCalls + f.Repository.ExistenceCalls);
        Assert.Equal(0, f.Cache.Count);
    }

    [Fact]
    public async Task A06_CacheHitStillValidatesInput()
    {
        using var f = new Fixture();
        await f.Service.GetSummaryAsync(new());
        var invalid = await f.Service.GetSummaryAsync(new() { From = "2026-09-14", To = "invalid" });
        Assert.Equal(AdminAiUsageResultStatus.InvalidQuery, invalid.Status);
        Assert.Equal(1, f.Repository.SystemCalls);
        Assert.Equal(1, f.Cache.Count);
    }

    [Fact]
    public async Task A08_A09_A10_A12_AggregatesArePreservedAndAverageUsesOnlyEvidence()
    {
        using var f = new Fixture();
        f.Repository.Data = new(new(8, 2, 1, 1, 1, 3, 40, 60, 5, 2, 2),
            [new(Day, 5, 1, 1, 30, 40, 2), new(Day.AddDays(-2), 3, 1, 2, 10, 20, 2)],
            [new("Explain", 2, 1, 10, 20), new("ParseRequest", 6, 1, 30, 40)], []);
        var a = (await f.Service.GetSummaryAsync(new() { From = "2026-10-11", To = "2026-10-13" })).Response!;
        Assert.Equal(8, a.Totals.Calls);
        Assert.Equal(2.5, a.Totals.AverageDurationMs);
        Assert.Equal(2, a.Totals.ActiveUsers);
        Assert.Equal(4, a.Days.Sum(row => row.ActiveUsers));
        Assert.Equal(3, a.Days.Sum(row => row.Failed));
        Assert.Equal(a.Totals.ProviderFailed + a.Totals.InvalidOutput + a.Totals.Abandoned, a.Days.Sum(row => row.Failed));
        Assert.Equal(a.Totals.Calls, a.Days.Sum(row => row.Calls));
        Assert.Equal(a.Totals.Calls, a.ByKind.Sum(row => row.Calls));
        Assert.Equal(new long[] { 3, 0, 5 }, a.Days.Select(row => row.Calls));
        Assert.Equal(new[] { "ParseRequest", "Explain" }, a.ByKind.Select(row => row.Kind));
        Assert.Equal(40, a.Totals.InputTokens);
        Assert.Equal(60, a.Totals.OutputTokens);
    }

    [Fact]
    public async Task A11_TopTenRepositoryOrderingIsPreserved()
    {
        using var f = new Fixture();
        var users = Enumerable.Range(1, 12).Select(i => new AdminAiUsageTopUser(
            Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), $"User {i}", $"u{i}@example.invalid", 5, i, i)).ToArray();
        f.Repository.Data = f.Repository.Data with { TopUsers = users };
        var response = (await f.Service.GetSummaryAsync(new())).Response!;
        Assert.Equal(users.Take(10), response.TopUsers);
    }

    [Fact]
    public async Task A13_NoEvidenceGivesNullAverageEvenWhenCallsExist()
    {
        using var f = new Fixture();
        f.Repository.Data = f.Repository.Data with { Totals = new(3, 0, 0, 0, 1, 2, 0, 0, 0, 0, 1) };
        Assert.Null((await f.Service.GetSummaryAsync(new())).Response!.Totals.AverageDurationMs);
    }

    [Fact]
    public async Task B01_C08_NonexistentUserStopsBeforeSummaryCallsAndSubscription()
    {
        using var f = new Fixture();
        f.Repository.Exists = false;
        Assert.Equal(AdminAiUsageResultStatus.UserNotFound, (await f.Service.GetUserSummaryAsync(UserId, new())).Status);
        Assert.Equal(AdminAiUsageResultStatus.UserNotFound, (await f.Service.GetUserCallsAsync(UserId, new())).Status);
        Assert.Equal(0, f.Repository.UserCalls + f.Repository.PageCalls + f.Subscription.Calls);
    }

    [Fact]
    public async Task B02_B03_B04_LockedExistingUserHasZeroHistoryButAuthoritativeToday()
    {
        using var f = new Fixture();
        // Repository contract includes locked accounts. Application must not add a status/plan filter.
        var b = await f.Service.GetUserSummaryAsync(UserId, new());
        Assert.Equal(AdminAiUsageResultStatus.Success, b.Status);
        Assert.Equal(UserId, f.Subscription.LastUserId);
        Assert.Equal(UserId, f.Repository.LastUserId);
        Assert.Equal(new AdminAiUsageTodayResponse(9, 17, Now.AddHours(29).UtcDateTime), b.Response!.Today);
        Assert.Equal(0, b.Response.Totals.Calls);
        Assert.Equal(30, b.Response.Days.Count);
        Assert.Null(b.Response.Totals.AverageDurationMs);
        Assert.Equal(1, f.Subscription.Calls);
    }

    [Fact]
    public async Task B05_SubscriptionNullAfterExistenceBecomesUserNotFound_NoFakeQuota()
    {
        using var f = new Fixture();
        f.Subscription.Result = null;
        var b = await f.Service.GetUserSummaryAsync(UserId, new());
        Assert.Equal(AdminAiUsageResultStatus.UserNotFound, b.Status);
        Assert.Null(b.Response);
        Assert.Equal(1, f.Repository.UserCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(30)]
    public async Task B04_ZeroAndNonzeroLimitsAreCopiedWithoutPlanInference(int limit)
    {
        using var f = new Fixture();
        f.Subscription.Result = f.Subscription.Result! with { Ai = new(0, limit, Now.AddDays(4).UtcDateTime) };
        var response = (await f.Service.GetUserSummaryAsync(UserId, new())).Response!;
        Assert.Equal(new AdminAiUsageTodayResponse(0, limit, Now.AddDays(4).UtcDateTime), response.Today);
    }

    [Fact]
    public async Task B06_C01_BAndCHaveNoCacheAndCDefaultsAreTyped()
    {
        using var f = new Fixture();
        await f.Service.GetUserSummaryAsync(UserId, new());
        await f.Service.GetUserSummaryAsync(UserId, new());
        await f.Service.GetUserCallsAsync(UserId, new());
        await f.Service.GetUserCallsAsync(UserId, new());
        Assert.Equal(2, f.Repository.UserCalls);
        Assert.Equal(2, f.Subscription.Calls);
        Assert.Equal(2, f.Repository.PageCalls);
        Assert.Equal(0, f.Cache.Count);
        var query = f.Repository.LastQuery!;
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Equal("startedAt", query.SortBy);
        Assert.Equal("desc", query.SortDirection);
        Assert.Null(query.From);
        Assert.Null(query.To);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("0001-01-01", null)]
    [InlineData(null, "9999-12-31")]
    [InlineData("0001-01-01", "9999-12-31")]
    [InlineData("2025-01-01", "2027-12-31")]
    public async Task C05_C06_C07_OptionalDatesAndNormalizationForwardedWithoutSearch(string? from, string? to)
    {
        using var f = new Fixture();
        var result = await f.Service.GetUserCallsAsync(UserId, new()
        {
            From = from, To = to, Search = new string('x', 5000), Kind = " Explain ", Status = " InProgress ",
            SortBy = " STARTEDAT ", SortDirection = " ASC ", Page = 2, PageSize = 7
        });
        Assert.Equal(AdminAiUsageResultStatus.Success, result.Status);
        var query = f.Repository.LastQuery!;
        Assert.Equal(from is null ? null : DateOnly.Parse(from), query.From);
        Assert.Equal(to is null ? null : DateOnly.Parse(to), query.To);
        Assert.Null(query.Search);
        Assert.Equal("Explain", query.Kind);
        Assert.Equal("InProgress", query.Status);
        Assert.Equal("asc", query.SortDirection);
        Assert.Equal("startedAt", query.SortBy);
        Assert.Equal(2, query.Page);
        Assert.Equal(7, query.PageSize);
    }

    [Fact]
    public async Task C09_BeyondLastPageMetadataIsForwardedWithoutClientPaging()
    {
        using var f = new Fixture();
        f.Repository.Page = new([], 10000, 7, 15, 3);
        var response = await f.Service.GetUserCallsAsync(UserId, new() { Page = 10000, PageSize = 7 });
        Assert.Same(f.Repository.Page, response.Response);
        Assert.Equal(15, response.Response!.TotalCount);
        Assert.Equal(3, response.Response.TotalPages);
    }

    [Fact]
    public async Task C10_V01_ValidationReturnsAllExpectedFieldsWithoutRepositoryCalls()
    {
        using var f = new Fixture();
        var result = await f.Service.GetUserCallsAsync(UserId, new()
        {
            Page = 0, PageSize = 101, SortBy = "model", SortDirection = "up", Kind = "Embedding", Status = "Released",
            From = "bad", To = "also bad", Search = new string('x', 5000)
        });
        Assert.Equal(AdminAiUsageResultStatus.InvalidQuery, result.Status);
        Assert.Equal(new[] { "From", "Kind", "Page", "PageSize", "SortBy", "SortDirection", "Status", "To" },
            result.ValidationErrors!.Keys.OrderBy(key => key));
        Assert.Equal(0, f.Repository.ExistenceCalls + f.Repository.PageCalls);
        Assert.Null(result.Response);
    }

    [Theory]
    [InlineData("0001-01-01", "0001-01-01", true)]
    [InlineData("0001-01-01", "0001-01-29", true)]
    [InlineData(null, "0001-01-01", false)]
    [InlineData(null, "0001-01-29", false)]
    [InlineData(null, "0001-01-30", true)]
    [InlineData("bad", "0001-01-01", false)]
    [InlineData("0001-01-02", "0001-01-01", false)]
    [InlineData("9999-12-31", "9999-12-31", false)]
    public async Task V02_ABExtremeDatesNeverThrow(string? from, string? to, bool success)
    {
        using var f = new Fixture();
        var query = new DashboardDateRangeQuery { From = from, To = to };
        var a = await f.Service.GetSummaryAsync(query);
        var b = await f.Service.GetUserSummaryAsync(UserId, query);
        var expected = success ? AdminAiUsageResultStatus.Success : AdminAiUsageResultStatus.InvalidQuery;
        Assert.Equal(expected, a.Status);
        Assert.Equal(expected, b.Status);
        if (success) Assert.Equal(DateOnly.MinValue, a.Response!.From);
        else Assert.NotEmpty(a.ValidationErrors!);
    }

    [Fact]
    public async Task V03_PublicShapesExcludeInternalAndSystemOnlyFields()
    {
        using var f = new Fixture();
        f.Repository.Data = new(new(1, 1, 0, 0, 0, 0, 11, 7, 123, 1, 1), [new(Day, 1, 1, 0, 11, 7, 1)],
            [new("ParseRequest", 1, 1, 11, 7)], [new(UserId, "User", "user@example.invalid", 1, 11, 7)]);
        f.Repository.Page = new([new(Guid.Empty, "ParseRequest", "Succeeded", Now.UtcDateTime, Now.UtcDateTime,
            123, "test-model", 11, 7, null)], 1, 20, 1, 1);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var a = JsonDocument.Parse(JsonSerializer.Serialize((await f.Service.GetSummaryAsync(new())).Response, options));
        using var b = JsonDocument.Parse(JsonSerializer.Serialize((await f.Service.GetUserSummaryAsync(UserId, new())).Response, options));
        using var calls = JsonDocument.Parse(JsonSerializer.Serialize((await f.Service.GetUserCallsAsync(UserId, new())).Response, options));
        AssertKeys(a.RootElement, "from,to,totals,byKind,days,topUsers,generatedAt");
        AssertKeys(a.RootElement.GetProperty("totals"), "calls,succeeded,providerFailed,invalidOutput,abandoned,inProgress,inputTokens,outputTokens,averageDurationMs,activeUsers");
        AssertKeys(a.RootElement.GetProperty("days")[0], "date,calls,succeeded,failed,inputTokens,outputTokens,activeUsers");
        AssertKeys(a.RootElement.GetProperty("byKind")[0], "kind,calls,succeeded,inputTokens,outputTokens");
        AssertKeys(a.RootElement.GetProperty("topUsers")[0], "userId,fullName,email,calls,inputTokens,outputTokens");
        AssertKeys(b.RootElement, "userId,from,to,today,totals,days,generatedAt");
        AssertKeys(b.RootElement.GetProperty("totals"), "calls,succeeded,providerFailed,invalidOutput,abandoned,inProgress,inputTokens,outputTokens,averageDurationMs");
        AssertKeys(b.RootElement.GetProperty("days")[0], "date,calls,succeeded,failed,inputTokens,outputTokens");
        AssertKeys(b.RootElement.GetProperty("today"), "used,limit,resetAt");
        AssertKeys(calls.RootElement, "items,page,pageSize,totalCount,totalPages");
        AssertKeys(calls.RootElement.GetProperty("items")[0], "id,kind,status,startedAt,completedAt,durationMs,model,inputTokens,outputTokens,tripId");
    }

    private static void AssertKeys(JsonElement element, string names) =>
        Assert.Equal(names.Split(',').OrderBy(name => name), element.EnumerateObject().Select(property => property.Name).OrderBy(name => name));

    private sealed class Fixture : IDisposable
    {
        public MutableClock Clock { get; }
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public FakeRepository Repository { get; } = new();
        public FakeSubscription Subscription { get; } = new();
        public AdminAiUsageService Service { get; }
        public Fixture(MutableClock? clock = null, IMemoryCache? cache = null)
        {
            Clock = clock ?? new();
            Service = new(Repository, Subscription, new DashboardDateRangeQueryValidator(Clock),
                new AdminAiCallsRequestValidator(), cache ?? Cache, Clock);
        }
        public void Dispose() => Cache.Dispose();
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = AdminAiUsageServiceTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeRepository : IAdminAiUsageRepository
    {
        public bool Exists { get; set; } = true;
        public bool FailNext { get; set; }
        public int SystemCalls { get; private set; }
        public int UserCalls { get; private set; }
        public int PageCalls { get; private set; }
        public int ExistenceCalls { get; private set; }
        public Guid LastUserId { get; private set; }
        public (DateOnly From, DateOnly To) LastRange { get; private set; }
        public AdminAiCallsQuery? LastQuery { get; private set; }
        public AdminAiUsageSummaryData Data { get; set; } = new(AdminAiUsageTotals.Empty, [], [], []);
        public PagedResult<AdminAiCallReadModel> Page { get; set; } = new([], 1, 20, 0, 0);
        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default)
        { ExistenceCalls++; LastUserId = userId; return Task.FromResult(Exists); }
        public Task<AdminAiUsageSummaryData> GetSystemSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            SystemCalls++; LastRange = (from, to);
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Deterministic repository failure"); }
            return Task.FromResult(Data);
        }
        public Task<AdminAiUsageSummaryData> GetUserSummaryAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default)
        { UserCalls++; LastUserId = userId; LastRange = (from, to); return Task.FromResult(Data); }
        public Task<PagedResult<AdminAiCallReadModel>> GetUserCallsAsync(Guid userId, AdminAiCallsQuery query, CancellationToken ct = default)
        { PageCalls++; LastUserId = userId; LastQuery = query; return Task.FromResult(Page); }
    }

    private sealed class FakeSubscription : ISubscriptionService
    {
        public int Calls { get; private set; }
        public Guid LastUserId { get; private set; }
        public SubscriptionMeResponse? Result { get; set; } = new("uninterpreted-plan", null,
            new(0, null, Now.UtcDateTime), new(0, null), new(9, 17, Now.AddHours(29).UtcDateTime));
        public Task<SubscriptionMeResponse?> GetMySubscriptionAsync(Guid userId, CancellationToken ct = default)
        { Calls++; LastUserId = userId; return Task.FromResult(Result); }
        public Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Plan catalog must not be queried.");
    }

    // Deterministic cache clock without sleeps or modifying MemoryCache's obsolete global clock option.
    private sealed class ControlledCache(MutableClock clock) : IMemoryCache
    {
        private readonly Dictionary<object, (object? Value, DateTimeOffset? Expiry)> values = [];
        public TimeSpan? LastTtl { get; private set; }
        public bool TryGetValue(object key, out object? value)
        {
            if (values.TryGetValue(key, out var item) && (!item.Expiry.HasValue || clock.Now < item.Expiry))
            { value = item.Value; return true; }
            values.Remove(key); value = null; return false;
        }
        public ICacheEntry CreateEntry(object key) => new Entry(this, key);
        public void Remove(object key) => values.Remove(key);
        public void Dispose() => values.Clear();
        private sealed class Entry(ControlledCache owner, object key) : ICacheEntry
        {
            public object Key { get; } = key;
            public object? Value { get; set; }
            public DateTimeOffset? AbsoluteExpiration { get; set; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
            public IList<IChangeToken> ExpirationTokens { get; } = new List<IChangeToken>();
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = new List<PostEvictionCallbackRegistration>();
            public CacheItemPriority Priority { get; set; }
            public long? Size { get; set; }
            public void Dispose()
            {
                owner.LastTtl = AbsoluteExpirationRelativeToNow;
                owner.values[Key] = (Value, AbsoluteExpirationRelativeToNow is { } ttl
                    ? owner.ClockNow.Add(ttl) : AbsoluteExpiration);
            }
        }
        private DateTimeOffset ClockNow => clock.Now;
    }
}
