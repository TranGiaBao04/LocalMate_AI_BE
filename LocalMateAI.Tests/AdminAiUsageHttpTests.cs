using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Reflection;
using FluentValidation;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Controllers;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.AdminAiUsage;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class AdminAiUsageHttpTests
{
    private const string User = "11111111-1111-1111-1111-111111111111";
    private const string A = "/api/admin/ai-usage/summary";
    private const string B = "/api/admin/users/" + User + "/ai-usage";
    private const string C = "/api/admin/users/" + User + "/ai-calls";

    [Theory]
    [InlineData("page=abc", "Page")]
    [InlineData("pageSize=abc", "PageSize")]
    [InlineData("page=2147483648", "Page")]
    [InlineData("pageSize=2147483648", "PageSize")]
    public async Task PrimitiveBindingFailure_UsesAiUsageContract(string query, string field)
    {
        using var host = new Host();
        host.Authenticate("users");
        using var response = await host.Client.GetAsync(C + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("code", out var code));
        Assert.Equal("invalid_ai_usage_query", code.GetString());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(0, host.Repository.PageCalls);
    }

    [Fact]
    public async Task PrimitiveBindingCorrectionDoesNotChangeOtherControllers()
    {
        using var host = new Host();
        using var response = await host.Client.GetAsync("/__ai_contract_test/model-binding?page=abc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
        Assert.False(json.RootElement.TryGetProperty("code", out _));
    }

    [Theory]
    [InlineData("none", 403)]
    [InlineData("revenue", 403)]
    [InlineData("locked", 403)]
    [InlineData("missing", 401)]
    public async Task BindingFilterCannotBypassAuthenticationOrPermissions(string mode, int status)
    {
        using var host = new Host();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(C + "?page=abc")).StatusCode);
        host.Authenticate(mode);
        Assert.Equal((HttpStatusCode)status, (await host.Client.GetAsync(C + "?page=abc")).StatusCode);
        Assert.Equal(0, host.Repository.TotalCalls);
    }

    [Fact]
    public async Task MultiplePrimitiveBindingErrorsUseCapitalizedFieldsWithoutEchoingValues()
    {
        using var host = new Host();
        host.Authenticate("users");
        using var json = await Problem(await host.Client.GetAsync(C + "?page=sensitive-invalid-value&pageSize=abc"),
            400, "invalid_ai_usage_query");
        Keys(json.RootElement.GetProperty("errors"), "Page,PageSize");
        Assert.DoesNotContain("sensitive-invalid-value", json.RootElement.ToString());
    }

    [Theory]
    [InlineData(A, "revenue", 200)]
    [InlineData(A, "users", 403)]
    [InlineData(A, "none", 403)]
    [InlineData(A, "role-admin", 403)]
    [InlineData(A, "both", 200)]
    [InlineData(B, "users", 200)]
    [InlineData(C, "users", 200)]
    [InlineData(B, "revenue", 403)]
    [InlineData(C, "revenue", 403)]
    [InlineData(B, "none", 403)]
    [InlineData(C, "none", 403)]
    [InlineData(B, "both", 200)]
    [InlineData(C, "both", 200)]
    [InlineData(A, "demo", 403)]
    [InlineData(B, "demo", 403)]
    [InlineData(C, "demo", 403)]
    public async Task H01_H06_PermissionMatrix_NoRoleShortcuts(string path, string mode, int status)
    {
        using var host = new Host();
        host.Authenticate(mode);
        using var response = await host.Client.GetAsync(path);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        if (status == 403) Assert.Equal(0, host.Repository.TotalCalls);
    }

    [Theory]
    [InlineData(A)]
    [InlineData(B)]
    [InlineData(C)]
    public async Task H07_H09_ExistingUnauthorizedLockedMissingBehavior(string path)
    {
        using var host = new Host();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(path)).StatusCode);
        host.Authenticate("locked");
        using var locked = await Problem(await host.Client.GetAsync(path), 403, "account_locked");
        Assert.Equal(path, locked.RootElement.GetProperty("instance").GetString());
        host.Authenticate("missing");
        using var missing = await Problem(await host.Client.GetAsync(path), 401, "account_not_found");
        Assert.Equal(0, host.Repository.TotalCalls);
    }

    [Theory]
    [InlineData(B)]
    [InlineData(C)]
    public async Task H10_LockedTargetUserRemainsQueryable(string path)
    {
        using var host = new Host();
        host.Authenticate("users");
        Assert.Equal(UserStatus.Locked, host.Repository.Target.Status);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(path)).StatusCode);
        Assert.Equal(Guid.Parse(User), host.Repository.LastUserId);
    }

    [Theory]
    [InlineData(A)]
    [InlineData(B)]
    public async Task SummaryDefaultsAndExplicitRangeHavePublicEmptyShape(string path)
    {
        using var host = new Host();
        host.Authenticate("both");
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("2026-09-14", root.GetProperty("from").GetString());
        Assert.Equal("2026-10-13", root.GetProperty("to").GetString());
        Assert.Equal(30, root.GetProperty("days").GetArrayLength());
        Assert.Equal(0, root.GetProperty("totals").GetProperty("calls").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("totals").GetProperty("averageDurationMs").ValueKind);
        Assert.Equal("2026-10-13T12:00:00Z", root.GetProperty("generatedAt").GetString());
        if (path == A)
        {
            Keys(root, "from,to,totals,byKind,days,topUsers,generatedAt");
            Keys(root.GetProperty("totals"), "calls,succeeded,providerFailed,invalidOutput,abandoned,inProgress,inputTokens,outputTokens,averageDurationMs,activeUsers");
            Keys(root.GetProperty("days")[0], "date,calls,succeeded,failed,inputTokens,outputTokens,activeUsers");
            Assert.Equal(new[] { "ParseRequest", "Explain" }, root.GetProperty("byKind").EnumerateArray().Select(row => row.GetProperty("kind").GetString()));
            Assert.Empty(root.GetProperty("topUsers").EnumerateArray());
        }
        else
        {
            Keys(root, "userId,from,to,today,totals,days,generatedAt");
            Keys(root.GetProperty("totals"), "calls,succeeded,providerFailed,invalidOutput,abandoned,inProgress,inputTokens,outputTokens,averageDurationMs");
            Keys(root.GetProperty("days")[0], "date,calls,succeeded,failed,inputTokens,outputTokens");
            Keys(root.GetProperty("today"), "used,limit,resetAt");
            Assert.Equal(2, root.GetProperty("today").GetProperty("used").GetInt32());
        }
        using var explicitResponse = await host.Client.GetAsync(path + "?from=2026-10-11&to=2026-10-13");
        Assert.Equal(HttpStatusCode.OK, explicitResponse.StatusCode);
        using var explicitJson = JsonDocument.Parse(await explicitResponse.Content.ReadAsStringAsync());
        Assert.Equal(3, explicitJson.RootElement.GetProperty("days").GetArrayLength());
        Assert.Equal((new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13)), host.Repository.LastRange);
    }

    [Theory]
    [InlineData("from=bad", "From")]
    [InlineData("to=bad", "To")]
    [InlineData("to=2026-10-14", "To")]
    [InlineData("from=2025-01-01&to=2026-10-13", "From")]
    [InlineData("from=2026-10-13&to=2026-10-12", "From")]
    [InlineData("to=0001-01-01", "From")]
    public async Task ABInvalidRangeUsesValidationProblemDetails(string query, string field)
    {
        using var host = new Host();
        host.Authenticate("both");
        foreach (var path in new[] { A, B })
        {
            using var json = await Problem(await host.Client.GetAsync(path + "?" + query), 400, "invalid_ai_usage_query", field);
            Assert.Equal(path, json.RootElement.GetProperty("instance").GetString());
        }
        Assert.Equal(0, host.Repository.TotalCalls);
    }

    [Theory]
    [InlineData("page=0", "Page")]
    [InlineData("page=10001", "Page")]
    [InlineData("pageSize=0", "PageSize")]
    [InlineData("pageSize=101", "PageSize")]
    [InlineData("sortBy=model", "SortBy")]
    [InlineData("sortDirection=up", "SortDirection")]
    [InlineData("kind=Embedding", "Kind")]
    [InlineData("kind=0", "Kind")]
    [InlineData("status=Released", "Status")]
    [InlineData("status=0", "Status")]
    [InlineData("from=2026-1-1", "From")]
    [InlineData("to=not-a-date", "To")]
    [InlineData("from=2026-10-14&to=2026-10-13", "From")]
    public async Task CInvalidQueriesUseCapitalizedFieldErrors(string query, string field)
    {
        using var host = new Host();
        host.Authenticate("users");
        using var json = await Problem(await host.Client.GetAsync(C + "?" + query), 400, "invalid_ai_usage_query", field);
        Assert.Equal(0, host.Repository.TotalCalls);
    }

    [Theory]
    [InlineData("", null, null, "desc")]
    [InlineData("from=2026-01-01", "2026-01-01", null, "desc")]
    [InlineData("to=2027-12-31", null, "2027-12-31", "desc")]
    [InlineData("from=0001-01-01&to=9999-12-31&sortDirection=asc", "0001-01-01", "9999-12-31", "asc")]
    [InlineData("sortBy=STARTEDAT&sortDirection=DESC", null, null, "desc")]
    public async Task COptionalDatesAndSortingAreNormalizedWithoutDefaultsOrRangeCap(string query, string? from, string? to, string direction)
    {
        using var host = new Host();
        host.Authenticate("users");
        using var response = await host.Client.GetAsync(C + "?" + query + "&search=" + new string('x', 5000));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var typed = host.Repository.LastQuery!;
        Assert.Equal(from is null ? null : DateOnly.Parse(from), typed.From);
        Assert.Equal(to is null ? null : DateOnly.Parse(to), typed.To);
        Assert.Equal(direction, typed.SortDirection);
        Assert.Equal("startedAt", typed.SortBy);
        Assert.Equal(1, typed.Page);
        Assert.Equal(20, typed.PageSize);
        Assert.Null(typed.Search);
    }

    [Theory]
    [InlineData("ParseRequest", "Succeeded")]
    [InlineData("Explain", "ProviderFailed")]
    [InlineData("Explain", "InvalidOutput")]
    [InlineData("ParseRequest", "Abandoned")]
    [InlineData("Explain", "InProgress")]
    public async Task CDocumentedFiltersAreForwarded(string kind, string status)
    {
        using var host = new Host();
        host.Authenticate("users");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(C + $"?kind={kind}&status={status}")).StatusCode);
        Assert.Equal(kind, host.Repository.LastQuery!.Kind);
        Assert.Equal(status, host.Repository.LastQuery.Status);
    }

    [Fact]
    public async Task CPublicSchemaAndBeyondLastPagePreserveMetadata()
    {
        using var host = new Host();
        host.Authenticate("users");
        host.Repository.IncludeRow = true;
        using var response = await host.Client.GetAsync(C);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Keys(json.RootElement, "items,page,pageSize,totalCount,totalPages");
        Keys(json.RootElement.GetProperty("items")[0], "id,kind,status,startedAt,completedAt,durationMs,model,inputTokens,outputTokens,tripId");
        host.Repository.TotalCount = 21;
        using var beyond = await host.Client.GetAsync(C + "?page=10000&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, beyond.StatusCode);
        using var page = JsonDocument.Parse(await beyond.Content.ReadAsStringAsync());
        Assert.Empty(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(10000, page.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, page.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(21, page.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.RootElement.GetProperty("totalPages").GetInt32());
    }

    [Theory]
    [InlineData(B)]
    [InlineData(C)]
    public async Task MissingTargetIs404_NotAnEmptySuccess(string path)
    {
        using var host = new Host();
        host.Authenticate("users");
        var unknown = path.Replace(User, "22222222-2222-2222-2222-222222222222");
        using var json = await Problem(await host.Client.GetAsync(unknown), 404, "user_not_found");
        Assert.Equal(unknown, json.RootElement.GetProperty("instance").GetString());
    }

    [Theory]
    [InlineData(A)]
    [InlineData(B)]
    [InlineData(C)]
    public async Task NoMutationVerbsExist(string path)
    {
        using var host = new Host();
        host.Authenticate("both");
        foreach (var verb in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(verb, path);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await host.Client.SendAsync(request)).StatusCode);
        }
        Assert.Equal(0, host.Repository.TotalCalls);
    }

    [Fact]
    public void ApiExplorerRoutesResponsesAndPermissionsAreActionSpecific()
    {
        using var host = new Host();
        Assert.Empty(typeof(AdminAiUsageController).GetCustomAttributes<HasPermissionAttribute>());
        var api = host.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(group => group.Items).Where(description => description.ActionDescriptor is ControllerActionDescriptor action
                && action.ControllerTypeInfo.AsType() == typeof(AdminAiUsageController)).ToArray();
        Assert.Equal(3, api.Length);
        Assert.Equal(new[] { "api/admin/ai-usage/summary", "api/admin/users/{userId}/ai-calls", "api/admin/users/{userId}/ai-usage" },
            api.Select(description => description.RelativePath).Order());
        foreach (var description in api)
        {
            Assert.Equal("GET", description.HttpMethod);
            var action = (ControllerActionDescriptor)description.ActionDescriptor;
            var global = description.RelativePath == "api/admin/ai-usage/summary";
            Assert.Equal(global ? Permissions.ViewRevenue : Permissions.ManageUsers,
                action.MethodInfo.GetCustomAttribute<HasPermissionAttribute>()!.Permission);
            var types = description.SupportedResponseTypes.ToDictionary(response => response.StatusCode, response => response.Type);
            Assert.Equal(global ? typeof(AdminAiUsageSystemResponse) : description.RelativePath!.EndsWith("ai-calls")
                ? typeof(PagedResult<AdminAiCallReadModel>) : typeof(AdminAiUsageUserResponse), types[200]);
            Assert.Equal(typeof(ValidationProblemDetails), types[400]);
            Assert.Equal(typeof(ProblemDetails), types[401]);
            Assert.Equal(typeof(ProblemDetails), types[403]);
            if (!global)
            {
                Assert.Contains("{userId:guid}", action.AttributeRouteInfo!.Template!);
                Assert.Equal(typeof(ProblemDetails), types[404]);
            }
        }
    }

    [Fact]
    public void ScopedDependenciesAndValidatorsResolve_ProgramOnlyAddsTheTwoRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<TimeProvider>(new Clock());
        services.AddSingleton<ISubscriptionService, Subscription>();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=localhost;Database=localmate_s1b_test_di;Username=test",
            npgsql => npgsql.UseNetTopologySuite()));
        services.AddScoped<IAdminAiUsageRepository, AdminAiUsageRepository>();
        services.AddScoped<IAdminAiUsageService, AdminAiUsageService>();
        services.AddValidatorsFromAssemblyContaining<AdminAiCallsRequestValidator>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var service = first.ServiceProvider.GetRequiredService<IAdminAiUsageService>();
        Assert.IsType<AdminAiUsageService>(service);
        Assert.Same(service, first.ServiceProvider.GetRequiredService<IAdminAiUsageService>());
        Assert.NotSame(service, second.ServiceProvider.GetRequiredService<IAdminAiUsageService>());
        Assert.IsType<AdminAiUsageRepository>(first.ServiceProvider.GetRequiredService<IAdminAiUsageRepository>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<IValidator<AdminAiCallsRequest>>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<IValidator<DashboardDateRangeQuery>>());
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LocalMateAI.API", "Program.cs"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "LocalMateAI.API", "Program.cs"));
        Assert.Equal(1, source.Split("AddScoped<IAdminAiUsageRepository, AdminAiUsageRepository>()").Length - 1);
        Assert.Equal(1, source.Split("AddScoped<IAdminAiUsageService, AdminAiUsageService>()").Length - 1);
    }

    [Fact]
    public async Task ControllerPreservesCancellationTokensForAllThreeServiceCalls()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new ServiceProbe();
        var controller = new AdminAiUsageController(service)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<BadRequestObjectResult>(await controller.GetSummaryAsync(new(), cancellation.Token));
        Assert.Equal(cancellation.Token, service.Token);
        Assert.IsType<NotFoundObjectResult>(await controller.GetUserSummaryAsync(Guid.Parse(User), new(), cancellation.Token));
        Assert.Equal(cancellation.Token, service.Token);
        Assert.IsType<OkObjectResult>(await controller.GetUserCallsAsync(Guid.Parse(User), new(), cancellation.Token));
        Assert.Equal(cancellation.Token, service.Token);
    }

    private sealed class ServiceProbe : IAdminAiUsageService
    {
        public CancellationToken Token { get; private set; }
        public Task<AdminAiUsageResult<AdminAiUsageSystemResponse>> GetSummaryAsync(DashboardDateRangeQuery query, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Task.FromResult(new AdminAiUsageResult<AdminAiUsageSystemResponse>(AdminAiUsageResultStatus.InvalidQuery,
                ValidationErrors: new Dictionary<string, string[]> { ["From"] = ["Test validation"] }));
        }
        public Task<AdminAiUsageResult<AdminAiUsageUserResponse>> GetUserSummaryAsync(Guid userId, DashboardDateRangeQuery query,
            CancellationToken cancellationToken = default)
        { Token = cancellationToken; return Task.FromResult(new AdminAiUsageResult<AdminAiUsageUserResponse>(AdminAiUsageResultStatus.UserNotFound)); }
        public Task<AdminAiUsageResult<PagedResult<AdminAiCallReadModel>>> GetUserCallsAsync(Guid userId, AdminAiCallsRequest query,
            CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Task.FromResult(new AdminAiUsageResult<PagedResult<AdminAiCallReadModel>>(AdminAiUsageResultStatus.Success,
                new([], 1, 20, 0, 0)));
        }
    }

    private static void Keys(JsonElement element, string keys) => Assert.Equal(keys.Split(',').Order(),
        element.EnumerateObject().Select(property => property.Name).Order());

    private static async Task<JsonDocument> Problem(HttpResponseMessage response, int status, string code, string? field = null)
    {
        using (response)
        {
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
            if (field is not null)
            {
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
                Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out var errors));
                Assert.NotEmpty(errors.EnumerateArray());
            }
            return json;
        }
    }

    private sealed class Host : IDisposable
    {
        private readonly WebApplication app;
        public HttpClient Client { get; }
        public IServiceProvider Services => app.Services;
        public Repository Repository { get; } = new();
        public Host()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "ContractTests" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IAdminAiUsageRepository>(Repository);
            builder.Services.AddSingleton<ISubscriptionService, Subscription>();
            builder.Services.AddSingleton<TimeProvider>(new Clock());
            builder.Services.AddMemoryCache();
            builder.Services.AddValidatorsFromAssemblyContaining<AdminAiCallsRequestValidator>();
            builder.Services.AddScoped<IAdminAiUsageService, AdminAiUsageService>();
            builder.Services.AddSingleton<IUserAccessService, Access>();
            builder.Services.AddAuthentication("AiUsageTest")
                .AddScheme<AuthenticationSchemeOptions, Authentication>("AiUsageTest", _ => { });
            builder.Services.AddLocalMateAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(AdminAiUsageController).Assembly)
                .AddApplicationPart(typeof(AdminAiUsageHttpTests).Assembly);
            app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseMiddleware<AccountAccessMiddleware>();
            app.UseAuthorization();
            app.MapControllers();
            app.StartAsync().GetAwaiter().GetResult();
            Client = app.GetTestClient();
        }
        public void Authenticate(string mode)
        { Client.DefaultRequestHeaders.Remove("X-Ai-Access"); Client.DefaultRequestHeaders.Add("X-Ai-Access", mode); }
        public void Dispose() { Client.Dispose(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var mode = Request.Headers["X-Ai-Access"].ToString();
            if (mode.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var id = mode switch { "revenue" => 1, "users" => 2, "locked" => 3, "missing" => 4, "both" => 5, _ => 6 };
            var claims = new List<Claim> { new("sub", $"00000000-0000-0000-0000-{id:D12}") };
            if (mode == "role-admin") claims.Add(new(ClaimTypes.Role, "Admin"));
            if (mode == "demo") claims.Add(new(AuthClaimNames.SessionType, AuthClaimNames.DemoSession));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class Access : IUserAccessService
    {
        public Task<UserAccessSnapshot?> GetSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var digit = id.ToString()[^1];
            HashSet<string> permissions = digit switch
            {
                '1' => [Permissions.ViewRevenue], '2' => [Permissions.ManageUsers],
                '3' or '5' => [Permissions.ViewRevenue, Permissions.ManageUsers], _ => []
            };
            return Task.FromResult<UserAccessSnapshot?>(digit == '4' ? null
                : new(digit == '3' ? UserStatus.Locked : UserStatus.Active, "NotAdmin", permissions));
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Repository : IAdminAiUsageRepository
    {
        public LocalMateAI.Domain.Entities.User Target { get; } = new() { Id = Guid.Parse(User), Status = UserStatus.Locked };
        public int PageCalls { get; private set; }
        public int TotalCalls { get; private set; }
        public bool IncludeRow { get; set; }
        public int TotalCount { get; set; } = 1;
        public Guid LastUserId { get; private set; }
        public AdminAiCallsQuery? LastQuery { get; private set; }
        public (DateOnly From, DateOnly To) LastRange { get; private set; }
        public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default)
        { TotalCalls++; LastUserId = userId; return Task.FromResult(userId == Target.Id); }
        public Task<AdminAiUsageSummaryData> GetSystemSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        { TotalCalls++; LastRange = (from, to); return Task.FromResult(new AdminAiUsageSummaryData(AdminAiUsageTotals.Empty, [], [], [])); }
        public Task<AdminAiUsageSummaryData> GetUserSummaryAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            GetSystemSummaryAsync(from, to, cancellationToken);
        public Task<PagedResult<AdminAiCallReadModel>> GetUserCallsAsync(Guid userId, AdminAiCallsQuery query, CancellationToken cancellationToken = default)
        {
            PageCalls++; TotalCalls++; LastUserId = userId; LastQuery = query;
            IReadOnlyList<AdminAiCallReadModel> items = IncludeRow && query.Page == 1
                ? [new(Guid.Empty, "Explain", "Succeeded", new Clock().GetUtcNow().UtcDateTime,
                    new Clock().GetUtcNow().UtcDateTime, 123, "test-model", 11, 7, null)] : [];
            return Task.FromResult(PagedResult<AdminAiCallReadModel>.Create(items, query.Page, query.PageSize,
                IncludeRow ? TotalCount : 0));
        }
    }

    private sealed class Subscription : ISubscriptionService
    {
        public Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Catalog access forbidden in this test.");
        public Task<SubscriptionMeResponse?> GetMySubscriptionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SubscriptionMeResponse?>(new("Test", null, new(0, 0, default), new(0, 0),
                new(2, 3, new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc))));
    }
}

// Test-only sentinel: proves normal ApiController binding stays unchanged outside the AI calls action.
[ApiController]
[Route("__ai_contract_test/model-binding")]
public sealed class AdminAiUsageBindingControlController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery] int page) => Ok(page);
}
