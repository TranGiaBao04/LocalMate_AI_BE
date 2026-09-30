using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Controllers;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class AdminPlansHttpTests
{
    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/features")]
    [InlineData("GET", "/11111111-1111-1111-1111-111111111111")]
    [InlineData("GET", "/11111111-1111-1111-1111-111111111111/versions")]
    [InlineData("POST", "")]
    [InlineData("PUT", "/11111111-1111-1111-1111-111111111111")]
    [InlineData("PUT", "/11111111-1111-1111-1111-111111111111/status")]
    [InlineData("DELETE", "/11111111-1111-1111-1111-111111111111")]
    public async Task AllRoutes_RequireAuthenticationAndManagePlans(string method, string suffix)
    {
        using var host = new Host(AdminPlanServiceTests.Service(new AdminPlanServiceTests.MemoryPlans()));
        async Task<HttpResponseMessage> Send() => await host.Client.SendAsync(new(new HttpMethod(method), "/api/admin/plans" + suffix)
        { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send()).StatusCode);
        host.Authenticate("no-permission");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send()).StatusCode);
    }

    [Theory]
    [InlineData("locked", 403, "account_locked")]
    [InlineData("missing", 401, "account_not_found")]
    [InlineData("demo", 403, null)]
    public async Task AccountAccess_IsPreserved(string mode, int status, string? code)
    {
        using var host = new Host(AdminPlanServiceTests.Service(new AdminPlanServiceTests.MemoryPlans()));
        host.Authenticate(mode);
        var result = await host.Client.GetAsync("/api/admin/plans");
        Assert.Equal((HttpStatusCode)status, result.StatusCode);
        if (code is not null) await Problem(result, status, code);
    }

    [Fact]
    public async Task ManagePlansWithoutAdminRole_CanUseAllEndpoints()
    {
        var repo = new AdminPlanServiceTests.MemoryPlans();
        using var host = new Host(AdminPlanServiceTests.Service(repo));
        host.Authenticate();
        var create = await host.Client.PostAsJsonAsync("/api/admin/plans", AdminPlanServiceTests.Create());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var plan = (await create.Content.ReadFromJsonAsync<AdminPlanResponse>())!;
        Assert.Equal($"/api/admin/plans/{plan.Id}", create.Headers.Location!.AbsolutePath);
        Assert.False(plan.IsActive);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(create.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/admin/plans?sortBy=name&page=1&pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/admin/plans/features")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync(create.Headers.Location,
            AdminPlanServiceTests.Update() with { Price = 59000 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync($"{create.Headers.Location}/status", new { isActive = true })).StatusCode);
        var versions = await host.Client.GetAsync($"{create.Headers.Location}/versions");
        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        using var json = JsonDocument.Parse(await versions.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("items")[0].GetProperty("versionNumber").GetInt32());
        Assert.True(json.RootElement.GetProperty("items")[0].GetProperty("isCurrent").GetBoolean());
        await Problem(await host.Client.DeleteAsync(create.Headers.Location), 409, "plan_in_use");
        var unpublished = new LocalMateAI.Domain.Entities.SubscriptionPlan { Code = "UNPUBLISHED", EntitlementPriority = 400 };
        repo.Plans.Add(unpublished.Id, unpublished);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/admin/plans/{unpublished.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("entitlementPriority")]
    [InlineData("isSystem")]
    [InlineData("versionNumber")]
    [InlineData("origin")]
    [InlineData("publishedAt")]
    [InlineData("currentVersionId")]
    public async Task Update_RejectsClientOwnedImmutableOrServerFields(string field)
    {
        using var host = new Host(AdminPlanServiceTests.Service(new AdminPlanServiceTests.MemoryPlans()));
        host.Authenticate();
        var result = await host.Client.PutAsJsonAsync("/api/admin/plans/11111111-1111-1111-1111-111111111111",
            new Dictionary<string, object> { [field] = "unexpected" });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("sortBy=Price")]
    [InlineData("sortDirection=random")]
    public async Task InvalidQuery_ProducesValidationProblem(string query)
    {
        using var host = new Host(AdminPlanServiceTests.Service(new AdminPlanServiceTests.MemoryPlans()));
        host.Authenticate();
        await Problem(await host.Client.GetAsync("/api/admin/plans?" + query), 400, "invalid_plan_data");
    }

    [Fact]
    public async Task ConflictsValidationAndNoopStatuses_HaveStableHttpCodes()
    {
        var repo = new AdminPlanServiceTests.MemoryPlans();
        var free = repo.AddFree();
        using var host = new Host(AdminPlanServiceTests.Service(repo));
        host.Authenticate();
        var path = "/api/admin/plans";
        await Problem(await host.Client.PostAsJsonAsync(path, AdminPlanServiceTests.Create() with { FeatureIds = [Guid.NewGuid()] }), 400, "invalid_plan_data");
        var first = (await (await host.Client.PostAsJsonAsync(path, AdminPlanServiceTests.Create())).Content.ReadFromJsonAsync<AdminPlanResponse>())!;
        await Problem(await host.Client.PostAsJsonAsync(path, AdminPlanServiceTests.Create(priority: 400)), 409, "plan_code_exists");
        await Problem(await host.Client.PostAsJsonAsync(path, AdminPlanServiceTests.Create(code: "OTHER")), 409, "plan_priority_exists");
        await Problem(await host.Client.PutAsJsonAsync($"{path}/{free.Id}/status", new { isActive = false }), 409, "free_cannot_deactivate");
        await Problem(await host.Client.DeleteAsync($"{path}/{free.Id}"), 409, "system_plan_locked");
        await Problem(await host.Client.GetAsync($"{path}/{Guid.NewGuid()}"), 404, "plan_not_found");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PutAsJsonAsync($"{path}/{first.Id}/status", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync($"{path}/{first.Id}/status", new { isActive = false })).StatusCode);
        Assert.Equal(0, repo.Saves);
    }

    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    internal sealed class Host : IDisposable
    {
        private readonly WebApplication app;
        internal HttpClient Client { get; }
        internal Host(IAdminPlanService service, IUserAccessService? accessService = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "ContractTests" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(service);
            builder.Services.AddSingleton<IUserAccessService>(accessService ?? new Access());
            builder.Services.AddAuthentication("PlanTest").AddScheme<AuthenticationSchemeOptions, Authentication>("PlanTest", _ => { });
            builder.Services.AddLocalMateAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(AdminPlansController).Assembly);
            app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseMiddleware<AccountAccessMiddleware>();
            app.UseAuthorization();
            app.MapControllers();
            app.StartAsync().GetAwaiter().GetResult();
            Client = app.GetTestClient();
        }
        internal void Authenticate(string mode = "manage")
        { Client.DefaultRequestHeaders.Remove("X-Plan-Access"); Client.DefaultRequestHeaders.Add("X-Plan-Access", mode); }
        public void Dispose() { Client.Dispose(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var mode = Request.Headers["X-Plan-Access"].ToString();
                if (mode.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
                var id = mode switch { "no-permission" => 2, "locked" => 3, "missing" => 4, _ => 1 };
                var claims = new List<Claim> { new("sub", $"00000000-0000-0000-0000-{id:D12}"), new(ClaimTypes.Role, "NotAdmin") };
                if (mode == "demo") claims.Add(new(AuthClaimNames.SessionType, AuthClaimNames.DemoSession));
                return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, "PlanTest")), "PlanTest")));
            }
        }
        private sealed class Access : IUserAccessService
        {
            public Task<UserAccessSnapshot?> GetSnapshotAsync(Guid id, CancellationToken ct = default) =>
                Task.FromResult<UserAccessSnapshot?>(id.ToString().EndsWith("4") ? null :
                    new(id.ToString().EndsWith("3") ? UserStatus.Locked : UserStatus.Active, "NotAdmin",
                        id.ToString().EndsWith("2") ? new HashSet<string>() : new HashSet<string> { Permissions.ManagePlans }));
        }
    }
}
