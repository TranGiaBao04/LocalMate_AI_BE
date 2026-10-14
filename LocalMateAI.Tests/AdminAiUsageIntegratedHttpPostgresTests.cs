using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Controllers;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Application.Validators.AdminAiUsage;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LocalMateAI.Tests;

public sealed class AdminAiUsageIntegratedHttpPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 13);
    private const string A = "/api/admin/ai-usage/summary";
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
    private static string B(int user = 10) => $"/api/admin/users/{Id(user)}/ai-usage";
    private static string C(int user = 10) => $"/api/admin/users/{Id(user)}/ai-calls";

    [Fact]
    public async Task ActualHttpRepositorySubscriptionAndLedgerReconcile()
    {
        await using var host = await Host.CreateAsync();
        host.Login(3);
        using var a = await host.Get(A);
        using var b = await host.Get(B());
        using var calls = await host.Get(C() + "?from=2026-10-13&to=2026-10-13");
        var totals = a.RootElement.GetProperty("totals");
        Assert.Equal(9, Number(totals, "calls"));
        Assert.Equal(9, a.RootElement.GetProperty("days").EnumerateArray().Sum(row => Number(row, "calls")));
        Assert.Equal(9, a.RootElement.GetProperty("byKind").EnumerateArray().Sum(row => Number(row, "calls")));
        Assert.Equal(9, new[] { "succeeded", "providerFailed", "invalidOutput", "abandoned", "inProgress" }.Sum(field => Number(totals, field)));
        Assert.Equal(2, Number(totals, "activeUsers"));
        Assert.Equal(66, Number(totals, "inputTokens"));
        Assert.Equal(42, Number(totals, "outputTokens"));
        Assert.Equal(1115d / 6, totals.GetProperty("averageDurationMs").GetDouble(), 9);
        Assert.Equal(2, Number(totals, "providerFailed"));
        Assert.Equal(1, Number(totals, "invalidOutput"));
        Assert.Equal(1, Number(totals, "abandoned"));
        Assert.Equal(4, a.RootElement.GetProperty("days").EnumerateArray().Sum(row => Number(row, "failed")));
        // Seven calls today, one of them a provider failure inside the free allowance: six charged.
        Assert.Equal(6, Number(b.RootElement.GetProperty("today"), "used"));
        Assert.Equal(7, Number(calls.RootElement, "totalCount"));
        var day = b.RootElement.GetProperty("days").EnumerateArray().Single(row => row.GetProperty("date").GetString() == "2026-10-13");
        Assert.Equal(7, Number(day, "calls"));
        using var scope = host.Services.CreateScope();
        Assert.IsType<AdminAiUsageRepository>(scope.ServiceProvider.GetRequiredService<IAdminAiUsageRepository>());
        Assert.IsType<AdminAiUsageService>(scope.ServiceProvider.GetRequiredService<IAdminAiUsageService>());
        var subscription = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
        Assert.IsType<SubscriptionService>(subscription);
        var me = (await subscription.GetMySubscriptionAsync(Id(10)))!;
        Assert.Equal(me.Ai.DailyUsed, Number(b.RootElement.GetProperty("today"), "used"));
        Assert.Equal(me.Ai.DailyLimit, Number(b.RootElement.GetProperty("today"), "limit"));
        Assert.Equal(me.Ai.ResetAt, b.RootElement.GetProperty("today").GetProperty("resetAt").GetDateTime());
        Assert.Equal(6, await scope.ServiceProvider.GetRequiredService<IAiUsageAccountingRepository>().CountDailyAsync(Id(10), Today));
        var items = calls.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.DoesNotContain(items, row => row.GetProperty("id").GetGuid() == Id(106));
        Assert.DoesNotContain(items, row => row.GetProperty("id").GetGuid() == Id(1103));
        Assert.Equal(5, items.Select(row => row.GetProperty("status").GetString()).Distinct().Count());
        Assert.Equal(0, Number(items.Single(row => row.GetProperty("status").GetString() == "Abandoned"), "inputTokens"));
        foreach (var row in items.Where(row => row.GetProperty("status").GetString() is "InProgress" or "Abandoned"))
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty("durationMs").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("model").ValueKind);
        }
    }

    [Fact]
    public async Task DefaultsEmptyDatesKindsAndTopTenOrderingMatchSpec()
    {
        await using var host = await Host.CreateAsync();
        await using (var c = host.Db.Context())
        {
            for (var i = 20; i < 32; i++)
            {
                c.Users.Add(new User { Id = Id(i), RoleId = Id(404), FullName = $"User {i}", Email = $"u{i}@example.invalid" });
                await c.SaveChangesAsync();
                await Log(c, i, 1000 + i, Now);
            }
        }
        host.Login(3);
        using var a = await host.Get(A);
        Assert.Equal("2026-09-14", a.RootElement.GetProperty("from").GetString());
        Assert.Equal(30, a.RootElement.GetProperty("days").GetArrayLength());
        Assert.Equal(new[] { "ParseRequest", "Explain" }, a.RootElement.GetProperty("byKind").EnumerateArray().Select(row => row.GetProperty("kind").GetString()));
        var top = a.RootElement.GetProperty("topUsers").EnumerateArray().ToArray();
        Assert.Equal(10, top.Length);
        Assert.Equal(8, Number(top[0], "calls"));
        Assert.Equal(new[] { Id(10), Id(11) }.Concat(Enumerable.Range(20, 8).Select(Id)), top.Select(row => row.GetProperty("userId").GetGuid()));
        using var empty = await host.Get(A + "?from=2026-09-01&to=2026-09-03");
        Assert.Equal(3, empty.RootElement.GetProperty("days").GetArrayLength());
        Assert.All(empty.RootElement.GetProperty("days").EnumerateArray(), row => Assert.Equal(0, Number(row, "calls")));
        Assert.Equal(2, empty.RootElement.GetProperty("byKind").GetArrayLength());
        Assert.Empty(empty.RootElement.GetProperty("topUsers").EnumerateArray());
        Assert.Null(empty.RootElement.GetProperty("totals").GetProperty("averageDurationMs").GetString());
    }

    [Fact]
    public async Task RealMemoryCacheExpiresAtFiveMinutes_NormalizedKeysAndLiveBCRemainIndependent()
    {
        await using var host = await Host.CreateAsync();
        host.Login(3);
        using var first = await host.Get(A);
        var reads = host.Trace.UsageReads;
        await using (var c = host.Db.Context()) await Log(c, 10, 900, Now);
        host.Clock.Now = new DateTimeOffset(Now.AddMinutes(4).AddSeconds(59));
        using var cached = await host.Get(A + "?from=2026-09-14&to=2026-10-13");
        Assert.Equal(reads, host.Trace.UsageReads);
        Assert.Equal(first.RootElement.ToString(), cached.RootElement.ToString());
        using var b = await host.Get(B());
        using var calls = await host.Get(C() + "?from=2026-10-13&to=2026-10-13");
        Assert.Equal(7, Number(b.RootElement.GetProperty("today"), "used"));
        Assert.Equal(8, Number(calls.RootElement, "totalCount"));
        host.Clock.Now = new DateTimeOffset(Now.AddMinutes(5));
        using var expired = await host.Get(A);
        Assert.Equal(10, Number(expired.RootElement.GetProperty("totals"), "calls"));
        Assert.NotEqual(first.RootElement.GetProperty("generatedAt").GetString(), expired.RootElement.GetProperty("generatedAt").GetString());
    }

    [Fact]
    public async Task DatabaseCommandFailureIs500AndNotCached_RetryQueriesRealData()
    {
        await using var host = await Host.CreateAsync();
        host.Login(3);
        host.Trace.FailNextUsage = true;
        using var error = await host.Get(A, HttpStatusCode.InternalServerError);
        Assert.Equal(500, Number(error.RootElement, "status"));
        await using (var c = host.Db.Context()) await Log(c, 10, 900, Now);
        using var retry = await host.Get(A);
        Assert.Equal(10, Number(retry.RootElement.GetProperty("totals"), "calls"));
        Assert.True(host.Trace.UsageReads > 1);
    }

    [Fact]
    public async Task ZeroUsageAndLockedTargetAndMissingUserAreDistinct()
    {
        await using var host = await Host.CreateAsync();
        host.Login(2);
        using var zero = await host.Get(B(12));
        Assert.Equal(0, Number(zero.RootElement.GetProperty("totals"), "calls"));
        Assert.Equal(0, Number(zero.RootElement.GetProperty("today"), "used"));
        Assert.Equal(3, Number(zero.RootElement.GetProperty("today"), "limit"));
        using var locked = await host.Get(B(11));
        Assert.Equal(1, Number(locked.RootElement.GetProperty("totals"), "calls"));
        using var empty = await host.Get(C(12));
        Assert.Equal(0, Number(empty.RootElement, "totalCount"));
        foreach (var path in new[] { B(999), C(999) })
        {
            using var missing = await host.Get(path, HttpStatusCode.NotFound);
            Assert.Equal("user_not_found", missing.RootElement.GetProperty("code").GetString());
        }
    }

    [Theory]
    [InlineData("", 8)]
    [InlineData("from=2026-10-13", 7)]
    [InlineData("to=2026-10-12", 1)]
    [InlineData("from=0001-01-01&to=9999-12-31", 8)]
    [InlineData("from=2020-01-01&to=2030-12-31", 8)]
    [InlineData("from=2026-10-13&to=2026-10-13&kind=Explain&status=Succeeded", 2)]
    public async Task SqlFiltersOptionalBoundsAndVietnamDateAreAppliedBeforeCount(string query, int count)
    {
        await using var host = await Host.CreateAsync();
        host.Login(2);
        using var result = await host.Get(C() + "?" + query + "&search=" + new string('x', 5000));
        Assert.Equal(count, Number(result.RootElement, "totalCount"));
        Assert.Equal(1, Number(result.RootElement, "page"));
        Assert.Equal(20, Number(result.RootElement, "pageSize"));
    }

    [Fact]
    public async Task SortPagingSourceCollisionAndBeyondLastPageExecuteInSql()
    {
        await using var host = await Host.CreateAsync();
        host.Login(2);
        using var descending = await host.Get(C());
        Assert.Equal(Id(105), descending.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        using var ascending = await host.Get(C() + "?sortDirection=asc");
        Assert.Equal(Id(201), ascending.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        await using (var c = host.Db.Context())
        {
            await Admission(c, 12, 500, AiUsageAdmissionState.Reserved, Now);
            await Log(c, 12, 500, Now);
            await Log(c, 12, 499, Now);
        }
        foreach (var direction in new[] { "asc", "desc" })
        {
            var ids = new List<Guid>();
            var states = new List<string>();
            for (var page = 1; page <= 3; page++)
            {
                using var result = await host.Get(C(12) + $"?page={page}&pageSize=1&sortDirection={direction}");
                Assert.Equal(3, Number(result.RootElement, "totalCount"));
                var row = result.RootElement.GetProperty("items")[0];
                ids.Add(row.GetProperty("id").GetGuid()); states.Add(row.GetProperty("status").GetString()!);
            }
            Assert.Equal(new[] { Id(499), Id(500), Id(500) }, ids);
            Assert.Equal(new[] { "Succeeded", "InProgress", "Succeeded" }, states);
        }
        using var beyond = await host.Get(C(12) + "?page=10000&pageSize=1");
        Assert.Empty(beyond.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(3, Number(beyond.RootElement, "totalCount"));
        Assert.Equal(3, Number(beyond.RootElement, "totalPages"));
    }

    [Theory]
    [InlineData("page=abc", "Page")]
    [InlineData("pageSize=abc", "PageSize")]
    [InlineData("page=0", "Page")]
    [InlineData("sortBy=model", "SortBy")]
    [InlineData("sortDirection=up", "SortDirection")]
    [InlineData("kind=Embedding", "Kind")]
    [InlineData("status=Released", "Status")]
    [InlineData("from=bad", "From")]
    [InlineData("to=2026-1-1", "To")]
    public async Task InvalidCallsHaveExactErrorCodeAndFields(string query, string field)
    {
        await using var host = await Host.CreateAsync(); host.Login(2);
        using var error = await host.Get(C() + "?" + query, HttpStatusCode.BadRequest);
        Assert.Equal("invalid_ai_usage_query", error.RootElement.GetProperty("code").GetString());
        Assert.True(error.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(0, host.Trace.UsageReads);
    }

    [Theory]
    [InlineData("from=bad", "From")]
    [InlineData("to=bad", "To")]
    [InlineData("to=2026-10-14", "To")]
    [InlineData("from=2020-01-01", "From")]
    public async Task InvalidSummaryRangesAre400BeforeReadingUsage(string query, string field)
    {
        await using var host = await Host.CreateAsync(); host.Login(3);
        foreach (var path in new[] { A, B() })
        {
            using var error = await host.Get(path + "?" + query, HttpStatusCode.BadRequest);
            Assert.Equal("invalid_ai_usage_query", error.RootElement.GetProperty("code").GetString());
            Assert.True(error.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        }
        Assert.Equal(0, host.Trace.UsageReads);
    }

    [Fact]
    public async Task SignedJwtAndDatabasePermissionsEnforceCallerIsolation()
    {
        await using var host = await Host.CreateAsync();
        foreach (var path in new[] { A, B(), C() }) Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(path)).StatusCode);
        host.Login(1);
        using var allowedA = await host.Get(A);
        foreach (var path in new[] { B(), C() }) Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(path)).StatusCode);
        host.Login(2);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(A)).StatusCode);
        using var allowedB = await host.Get(B()); using var allowedC = await host.Get(C());
        host.Login(4);
        foreach (var path in new[] { A, B(), C() }) Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(path)).StatusCode);
        host.Login(5);
        using var locked = await host.Get(A, HttpStatusCode.Forbidden);
        Assert.Equal("account_locked", locked.RootElement.GetProperty("code").GetString());
        host.Login(999);
        using var absent = await host.Get(A, HttpStatusCode.Unauthorized);
        Assert.Equal("account_not_found", absent.RootElement.GetProperty("code").GetString());
        host.Login(3, wrongSignature: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(A)).StatusCode);
        host.Login(3, expired: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(A)).StatusCode);
    }

    [Fact]
    public async Task PublicJsonHasNoInternalFieldsAndModelMatchesExistingMigrations()
    {
        await using var host = await Host.CreateAsync(); host.Login(3);
        foreach (var path in new[] { A, B(), C() })
        {
            using var json = await host.Get(path);
            var text = json.RootElement.ToString();
            foreach (var field in new[] { "sourceRank", "fencingToken", "fencingGeneration", "reservedUntil", "recoveryAfter",
                "resolvedPlanVersionId", "durationSumMs", "durationCount", "prompt", "response", "validationErrors" })
                Assert.DoesNotContain($"\"{field}\"", text);
        }
        await using var c = host.Db.Context();
        Assert.False(c.Database.HasPendingModelChanges());
        Assert.Empty(await c.Database.GetPendingMigrationsAsync());
    }

    private static long Number(JsonElement element, string field) => element.GetProperty(field).GetInt64();

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly SymmetricSecurityKey key = new(RandomNumberGenerator.GetBytes(32));
        public IsolatedPlanDatabase Db { get; }
        public MutableClock Clock { get; } = new();
        public UsageTrace Trace { get; } = new();
        public HttpClient Client { get; }
        public IServiceProvider Services => app.Services;
        private Host(IsolatedPlanDatabase db)
        {
            Db = db;
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "ContractTests" });
            builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(db.Connection,
                npgsql => npgsql.UseNetTopologySuite()).AddInterceptors(Trace));
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.AddMemoryCache(options => options.Clock = Clock);
            builder.Services.AddScoped<IAdminAiUsageRepository, AdminAiUsageRepository>();
            builder.Services.AddScoped<IAdminAiUsageService, AdminAiUsageService>();
            builder.Services.AddScoped<IUserRepository, UserRepository>();
            builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
            builder.Services.AddScoped<IUsageEventRepository, UsageEventRepository>();
            builder.Services.AddScoped<ITripRepository, TripRepository>();
            builder.Services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
            builder.Services.AddScoped<ISystemSettingProvider, SystemSettingProvider>();
            builder.Services.AddScoped<IAiUsageAdmissionRepository, AiUsageAdmissionRepository>();
            builder.Services.AddScoped<IAiUsageAccountingRepository>(sp => sp.GetRequiredService<IAiUsageAdmissionRepository>());
            builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
            builder.Services.AddScoped<IUserAccessRepository, UserAccessRepository>();
            builder.Services.AddScoped<IUserAccessService, UserAccessService>();
            builder.Services.AddValidatorsFromAssemblyContaining<AdminAiCallsRequestValidator>();
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidateIssuer = true,
                    ValidIssuer = "s4-test", ValidateAudience = true, ValidAudience = "s4-test-client",
                    ValidateLifetime = true, RequireSignedTokens = true, RequireExpirationTime = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["JWT"],
                    ClockSkew = TimeSpan.FromMinutes(1), NameClaimType = "sub", RoleClaimType = "role"
                };
            });
            builder.Services.AddLocalMateAuthorization();
            builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
            builder.Services.AddControllers().AddApplicationPart(typeof(AdminAiUsageController).Assembly);
            app = builder.Build(); app.UseExceptionHandler(); app.UseRouting(); app.UseAuthentication();
            app.UseMiddleware<AccountAccessMiddleware>(); app.UseAuthorization(); app.MapControllers();
            app.StartAsync().GetAwaiter().GetResult(); Client = app.GetTestClient();
        }
        public static async Task<Host> CreateAsync()
        {
            var db = await IsolatedPlanDatabase.CreateAsync();
            try { await Seed(db); return new Host(db); }
            catch { await db.DisposeAsync(); throw; }
        }
        public void Login(int user, bool wrongSignature = false, bool expired = false)
        {
            var time = DateTime.UtcNow;
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "s4-test", Audience = "s4-test-client", TokenType = "JWT",
                Subject = new ClaimsIdentity([new("sub", Id(user).ToString())]),
                IssuedAt = time.AddMinutes(-10), NotBefore = time.AddMinutes(-10),
                Expires = expired ? time.AddMinutes(-5) : time.AddHours(1),
                SigningCredentials = new(wrongSignature ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) : key,
                    SecurityAlgorithms.HmacSha256)
            });
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        public async Task<JsonDocument> Get(string path, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var response = await Client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
            return JsonDocument.Parse(body);
        }
        public async ValueTask DisposeAsync()
        { Client.Dispose(); await app.DisposeAsync(); await Db.DisposeAsync(); }
    }

    private sealed class MutableClock : TimeProvider, Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset Now { get; set; } = new(NowUtc);
        private static DateTime NowUtc => AdminAiUsageIntegratedHttpPostgresTests.Now;
        public DateTimeOffset UtcNow => Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class UsageTrace : DbCommandInterceptor
    {
        public int UsageReads { get; private set; }
        public bool FailNextUsage { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"AiUsageAdmissions\"", StringComparison.Ordinal))
            {
                UsageReads++;
                if (FailNextUsage) { FailNextUsage = false; throw new Npgsql.NpgsqlException("Injected test-only database command failure."); }
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task Seed(IsolatedPlanDatabase db)
    {
        await using var c = db.Context();
        string[][] permissions = [[Permissions.ViewRevenue], [Permissions.ManageUsers], [Permissions.ViewRevenue, Permissions.ManageUsers], []];
        for (var i = 0; i < 4; i++)
        {
            var role = new Role { Id = Id(401 + i), Name = $"Audit Role {i}", NormalizedName = $"AUDIT ROLE {i}" };
            foreach (var permission in permissions[i]) role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
            c.Roles.Add(role);
        }
        await c.SaveChangesAsync();
        foreach (var id in new[] { 1, 2, 3, 4, 5, 10, 11, 12 })
        {
            var role = id switch { 1 => 401, 2 => 402, 3 or 5 => 403, _ => 404 };
            c.Users.Add(new User { Id = Id(id), RoleId = Id(role), FullName = $"User {id}", Email = $"user{id}@example.invalid",
                Status = id is 5 or 11 ? UserStatus.Locked : UserStatus.Active });
        }
        await c.SaveChangesAsync();
        var trip = await PostgresTestDatabase.InsertTripAsync(c, Id(10));
        await Admission(c, 10, 100, AiUsageAdmissionState.Reserved, Now.AddHours(-1));
        await Admission(c, 10, 101, AiUsageAdmissionState.DispatchAuthorized, Now.AddHours(-1).AddMinutes(1), tripId: trip.Id);
        await Admission(c, 10, 102, AiUsageAdmissionState.Abandoned, Now.AddHours(-1).AddMinutes(2), tripId: trip.Id);
        await Admission(c, 10, 103, AiUsageAdmissionState.Completed, Now.AddHours(-1).AddMinutes(3), LlmCallOutcome.Succeeded, trip.Id);
        await Admission(c, 10, 104, AiUsageAdmissionState.Completed, Now.AddHours(-1).AddMinutes(4), LlmCallOutcome.ProviderFailed);
        await Admission(c, 10, 105, AiUsageAdmissionState.Completed, Now.AddHours(-1).AddMinutes(5), LlmCallOutcome.InvalidOutput);
        await Admission(c, 10, 106, AiUsageAdmissionState.Released, Now.AddHours(-1).AddMinutes(6));
        var midnight = new DateTime(2026, 10, 12, 17, 0, 0, DateTimeKind.Utc);
        await Log(c, 10, 200, midnight, kind: LlmCallKind.Explain, duration: 500);
        await Log(c, 10, 201, midnight.AddMicroseconds(-1), LlmCallOutcome.ProviderFailed);
        await Log(c, 11, 202, Now, kind: LlmCallKind.Explain);
    }

    private static async Task Log(AppDbContext c, int user, int id, DateTime at,
        LlmCallOutcome outcome = LlmCallOutcome.Succeeded, LlmCallKind kind = LlmCallKind.ParseRequest, Guid? tripId = null, int duration = 123)
    {
        c.LlmCallLogs.Add(new LlmCallLog { Id = Id(id), UserId = Id(user), Kind = kind, TripId = tripId,
            Outcome = outcome, Model = "test-provider", InputTokens = 11, OutputTokens = 7, DurationMilliseconds = duration });
        await c.SaveChangesAsync();
        await c.LlmCallLogs.Where(log => log.Id == Id(id)).ExecuteUpdateAsync(set => set.SetProperty(log => log.CreatedAt, at));
    }

    private static async Task Admission(AppDbContext c, int user, int id, AiUsageAdmissionState state, DateTime at,
        LlmCallOutcome? outcome = null, Guid? tripId = null)
    {
        if (state == AiUsageAdmissionState.Completed)
            await Log(c, user, id + 1000, at.AddDays(1), outcome!.Value,
                tripId.HasValue ? LlmCallKind.Explain : LlmCallKind.ParseRequest, tripId);
        c.AiUsageAdmissions.Add(new AiUsageAdmission { Id = Id(id), UserId = Id(user), State = state,
            Kind = tripId.HasValue ? LlmCallKind.Explain : LlmCallKind.ParseRequest, TripId = tripId, TripIdSnapshot = tripId,
            Outcome = outcome, VietnamUsageDate = DateOnly.FromDateTime(at.AddHours(7)), AdmittedAt = at,
            ReservedUntil = at.AddSeconds(30), ResolvedPlanVersionId = SubscriptionBaseline.VersionId(PlanCode.Free),
            AdmittedDailyLimit = 3, AdmittedExplainLimit = tripId.HasValue ? 1 : null,
            FencingToken = Id(id + 3000), FencingGeneration = 1,
            DispatchAuthorizedAt = state is AiUsageAdmissionState.Completed or AiUsageAdmissionState.DispatchAuthorized or AiUsageAdmissionState.Abandoned
                ? at.AddSeconds(1) : null,
            CompletedAt = state is AiUsageAdmissionState.Completed or AiUsageAdmissionState.Abandoned or AiUsageAdmissionState.Released ? at.AddSeconds(2) : null,
            LlmCallLogId = state == AiUsageAdmissionState.Completed ? Id(id + 1000) : null });
        await c.SaveChangesAsync();
    }
}
