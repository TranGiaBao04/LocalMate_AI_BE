using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Controllers;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Interfaces.Payments;
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

public sealed class AdminTransactionsHttpTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/summary")]
    [InlineData("/export.csv")]
    public async Task AllEndpoints_RequireViewRevenue_NotRoleOrManagePlans(string path)
    {
        using var host = new Host();
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/admin/transactions" + path)).StatusCode);
        foreach (var mode in new[] { "none", "manage", "demo" })
        {
            host.Authenticate(mode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/admin/transactions" + path)).StatusCode);
        }
        host.Authenticate("revenue");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/admin/transactions" + path)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/summary")]
    [InlineData("/export.csv")]
    public async Task LockedAndMissingAccounts_KeepExistingMiddlewareBehavior(string path)
    {
        using var host = new Host();
        host.Authenticate("locked");
        await Problem(await host.Client.GetAsync("/api/admin/transactions" + path), 403, "account_locked");
        host.Authenticate("missing");
        await Problem(await host.Client.GetAsync("/api/admin/transactions" + path), 401, "account_not_found");
    }

    [Theory]
    [InlineData("status=bad")]
    [InlineData("status=1")]
    [InlineData("operationType=SingleItinerary")]
    [InlineData("operationType=0")]
    [InlineData("createdFrom=2026-10-01")]
    [InlineData("createdTo=2026-10-01T10:00:00")]
    [InlineData("createdFrom=2026-10-02T00:00:00Z&createdTo=2026-10-01T00:00:00Z")]
    public async Task InvalidCanonicalFilters_AreStable400ForEveryEndpoint(string query)
    {
        using var host = new Host();
        host.Authenticate();
        foreach (var path in new[] { "", "/summary", "/export.csv" })
            await Problem(await host.Client.GetAsync("/api/admin/transactions" + path + "?" + query), 400, "invalid_transaction_query");
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=10001")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("sortBy=rawPayload")]
    [InlineData("sortDirection=bad")]
    public async Task InvalidListPagingSort_AreValidationProblems(string query)
    {
        using var host = new Host();
        host.Authenticate();
        await Problem(await host.Client.GetAsync("/api/admin/transactions?" + query), 400, "invalid_transaction_query");
    }

    [Fact]
    public async Task BindingFailure_Is400WithoutInternalDetails()
    {
        using var host = new Host();
        host.Authenticate();
        var response = await host.Client.GetAsync("/api/admin/transactions?page=not-an-integer");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task List_HasOnlyTheApprovedH7ReadContract()
    {
        using var host = new Host();
        host.Authenticate();
        var response = await host.Client.GetAsync("/api/admin/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = json.RootElement.GetProperty("items")[0];
        string[] names = ["id", "providerOrderCode", "userId", "userFullName", "userEmail", "productKind", "planCode",
            "planName", "operationType", "status", "amount", "currency", "createdAt", "expiresAt", "paidAt"];
        Assert.Equal(names.Order(), row.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("SubscriptionPlan", row.GetProperty("productKind").GetString());
        Assert.Equal("Purchase", row.GetProperty("operationType").GetString());
        Assert.Equal("VND", row.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task SummaryAndCsv_IgnoreListPageAndCsvHasCorrectHeaders()
    {
        using var host = new Host();
        host.Authenticate();
        var summary = await host.Client.GetAsync("/api/admin/transactions/summary?page=100&pageSize=1&sortBy=unknown");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var value = (await summary.Content.ReadFromJsonAsync<AdminTransactionSummary>())!;
        Assert.Equal(1, value.TotalTransactions);
        Assert.Equal(19000m, value.GrossRevenue);
        var csv = await host.Client.GetAsync("/api/admin/transactions/export.csv?page=100&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", csv.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", csv.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("localmate-transactions-20261001-020304.csv", csv.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(2, AdminTransactionTests.ParseCsv(await csv.Content.ReadAsByteArrayAsync()).Count);
    }

    [Fact]
    public async Task OverLimit_ReturnsProblemMetadata_NotPartialFile()
    {
        using var host = new Host(AdminTransactionTests.Service(new AdminTransactionTests.RecordingRepository { ExportCount = 10001 }));
        host.Authenticate();
        var response = await host.Client.GetAsync("/api/admin/transactions/export.csv");
        await Problem(response, 400, "transaction_export_limit_exceeded");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(10000, json.RootElement.GetProperty("maxRows").GetInt32());
        Assert.Equal(10001, json.RootElement.GetProperty("matchingRows").GetInt32());
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task OffsetTimestamp_QueryBindingNormalizesToSameUtcFilterOnAllRoutes()
    {
        var repository = new AdminTransactionTests.RecordingRepository();
        using var host = new Host(AdminTransactionTests.Service(repository));
        host.Authenticate();
        var query = "?createdFrom=" + Uri.EscapeDataString("2026-10-01T09:00:00+07:00")
            + "&createdTo=" + Uri.EscapeDataString("2026-10-01T10:00:00+07:00");
        foreach (var path in new[] { "", "/summary", "/export.csv" })
            Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/admin/transactions" + path + query)).StatusCode);
        Assert.Equal(3, repository.Filters.Count);
        Assert.All(repository.Filters, f => Assert.Equal(new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc), f.CreatedFromUtc));
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
        internal Host(IAdminTransactionService? service = null, IPaymentReconciliationService? reconciliation = null,
            IEntitlementRepairService? repair = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "ContractTests" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(service ?? AdminTransactionTests.Service(new AdminTransactionTests.RecordingRepository()));
            if (reconciliation is not null) builder.Services.AddSingleton(reconciliation);
            if (repair is not null) builder.Services.AddSingleton(repair);
            builder.Services.AddSingleton<IUserAccessService, Access>();
            builder.Services.AddAuthentication("TransactionTest")
                .AddScheme<AuthenticationSchemeOptions, Authentication>("TransactionTest", _ => { });
            builder.Services.AddLocalMateAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(AdminTransactionsController).Assembly)
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
            app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseMiddleware<AccountAccessMiddleware>();
            app.UseAuthorization();
            app.MapControllers();
            app.StartAsync().GetAwaiter().GetResult();
            Client = app.GetTestClient();
        }
        internal void Authenticate(string mode = "revenue")
        { Client.DefaultRequestHeaders.Remove("X-Transaction-Access"); Client.DefaultRequestHeaders.Add("X-Transaction-Access", mode); }
        public void Dispose() { Client.Dispose(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var mode = Request.Headers["X-Transaction-Access"].ToString();
                if (mode.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
                var id = mode switch { "none" => 2, "manage" => 3, "locked" => 4, "missing" => 5, _ => 1 };
                var claims = new List<Claim> { new("sub", $"00000000-0000-0000-0000-{id:D12}"), new(ClaimTypes.Role, "NotAdmin") };
                if (mode == "demo") claims.Add(new(AuthClaimNames.SessionType, AuthClaimNames.DemoSession));
                return Task.FromResult(AuthenticateResult.Success(new(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, "TransactionTest")), "TransactionTest")));
            }
        }
        private sealed class Access : IUserAccessService
        {
            public Task<UserAccessSnapshot?> GetSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
            {
                var last = id.ToString()[^1];
                return Task.FromResult<UserAccessSnapshot?>(last == '5' ? null :
                    new(last == '4' ? UserStatus.Locked : UserStatus.Active, "NotAdmin",
                        last == '2' ? new HashSet<string>() : last == '3' ? new HashSet<string> { Permissions.ManagePlans }
                            : new HashSet<string> { Permissions.ViewRevenue }));
            }
        }
    }
}
