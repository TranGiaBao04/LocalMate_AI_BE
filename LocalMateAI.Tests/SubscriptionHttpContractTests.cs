using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMateAI.API.Controllers;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class SubscriptionHttpContractTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Plans_AnonymousRequest_ReturnsThreeSerializedPlans()
    {
        using var host = new ContractTestHost();

        var response = await host.Client.GetAsync("/api/subscription/plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var codes = json.RootElement.EnumerateArray()
            .Select(plan => plan.GetProperty("code").GetString()!)
            .ToArray();
        Assert.Equal(new[] { "Free", "TripPass", "Membership" }, codes);
    }

    [Theory]
    [InlineData("GET", "/api/subscription/me")]
    [InlineData("POST", "/api/subscription/checkout")]
    [InlineData("POST", "/api/subscription/renew")]
    [InlineData("GET", "/api/subscription/orders/22222222-2222-2222-2222-222222222222")]
    public async Task ProtectedSubscriptionRoutes_AnonymousRequest_ReturnUnauthorized(
        string method,
        string path)
    {
        using var host = new ContractTestHost();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (path.EndsWith("checkout", StringComparison.Ordinal))
        {
            request.Content = JsonContent.Create(new { planCode = "TripPass" });
        }

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetOrder_InvalidId_ReturnsInvalidIdProblem(string orderId)
    {
        using var host = new ContractTestHost();
        Authenticate(host.Client);

        var response = await host.Client.GetAsync($"/api/subscription/orders/{orderId}");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_id");
    }

    [Fact]
    public async Task GetMe_NonPersistedUser_ReturnsPersistedAccountRequired()
    {
        using var host = new ContractTestHost();
        host.Subscription.MeResponse = null;
        Authenticate(host.Client);

        var response = await host.Client.GetAsync("/api/subscription/me");

        await AssertProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "persisted_account_required");
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Admin")]
    public async Task GetMe_PersistedAuthorizedRole_ReturnsSerializedSubscription(string role)
    {
        using var host = new ContractTestHost();
        Authenticate(host.Client, role);

        var response = await host.Client.GetAsync("/api/subscription/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Free", json.RootElement.GetProperty("plan").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("usage").GetProperty("generateLimit").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("savedTrips").GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData(PaymentIntentResultStatus.InvalidPlanCode, 400, "invalid_plan_code")]
    [InlineData(PaymentIntentResultStatus.PlanAlreadyActive, 409, "plan_already_active")]
    [InlineData(PaymentIntentResultStatus.CoveredByHigherPlan, 409, "already_covered_by_higher_plan")]
    [InlineData(PaymentIntentResultStatus.GatewayUnavailable, 502, "payment_gateway_unavailable")]
    [InlineData(PaymentIntentResultStatus.NonPersistedUser, 403, "persisted_account_required")]
    public async Task Checkout_ServiceError_MapsToHttpProblem(
        PaymentIntentResultStatus resultStatus,
        int expectedStatus,
        string expectedCode)
    {
        using var host = new ContractTestHost();
        host.Payment.CheckoutResult = new PaymentIntentResult(resultStatus);
        Authenticate(host.Client);

        var response = await host.Client.PostAsJsonAsync(
            "/api/subscription/checkout",
            new { planCode = "TripPass" });

        await AssertProblemAsync(response, (HttpStatusCode)expectedStatus, expectedCode);
    }

    [Fact]
    public async Task Checkout_PendingOrderExists_ReturnsReusableOrderDetails()
    {
        using var host = new ContractTestHost();
        var expiresAt = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var orderId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        host.Payment.CheckoutResult = new PaymentIntentResult(
            PaymentIntentResultStatus.PendingOrderExists,
            new PaymentIntentResponse(
                orderId,
                "controlled-qr-code",
                "https://checkout.test/order",
                59000m,
                expiresAt));
        Authenticate(host.Client);

        var response = await host.Client.PostAsJsonAsync(
            "/api/subscription/checkout",
            new { planCode = "Membership" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("pending_order_exists", root.GetProperty("code").GetString());
        Assert.Equal(orderId, root.GetProperty("orderId").GetGuid());
        Assert.Equal("controlled-qr-code", root.GetProperty("qrCode").GetString());
        Assert.Equal("https://checkout.test/order", root.GetProperty("checkoutUrl").GetString());
        Assert.Equal(59000m, root.GetProperty("amount").GetDecimal());
        Assert.Equal(expiresAt, root.GetProperty("expiresAt").GetDateTime());
    }

    [Fact]
    public async Task Renew_NoActiveSubscription_ReturnsConflictWithoutRequestBody()
    {
        using var host = new ContractTestHost();
        host.Payment.RenewResult = new PaymentIntentResult(
            PaymentIntentResultStatus.NoActiveSubscription);
        Authenticate(host.Client);

        var response = await host.Client.PostAsync("/api/subscription/renew", content: null);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "no_active_subscription");
    }

    [Fact]
    public async Task GetOrder_NotFound_ReturnsPaymentOrderNotFound()
    {
        using var host = new ContractTestHost();
        host.Payment.OrderResult = new PaymentOrderLookupResult(
            PaymentOrderLookupStatus.NotFound);
        Authenticate(host.Client);

        var response = await host.Client.GetAsync(
            "/api/subscription/orders/44444444-4444-4444-4444-444444444444");

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "payment_order_not_found");
    }

    [Fact]
    public async Task PayOSWebhook_InvalidSignature_IsAnonymousAndReturnsBadRequest()
    {
        using var host = new ContractTestHost();
        host.Webhook.Result = new PaymentWebhookResult(
            PaymentWebhookStatus.InvalidSignature);

        var response = await host.Client.PostAsJsonAsync(
            "/api/payments/payos/webhook",
            new { controlled = true });

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_webhook_signature");
    }

    [Fact]
    public async Task PayOSWebhook_Acknowledged_IsAnonymousAndReturnsSuccess()
    {
        using var host = new ContractTestHost();
        host.Webhook.Result = new PaymentWebhookResult(PaymentWebhookStatus.Acknowledged);

        var response = await host.Client.PostAsJsonAsync(
            "/api/payments/payos/webhook",
            new { controlled = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
    }

    private static void Authenticate(HttpClient client, string role = "User")
    {
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RoleHeader, role);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    private sealed class ContractTestHost : IDisposable
    {
        private readonly WebApplication application;

        public ContractTestHost()
        {
            Subscription = new StubSubscriptionService();
            Payment = new StubPaymentService();
            Webhook = new StubPaymentWebhookService();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "ContractTests"
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<ISubscriptionService>(Subscription);
            builder.Services.AddSingleton<IPaymentService>(Payment);
            builder.Services.AddSingleton<IPaymentWebhookService>(Webhook);
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(SubscriptionController).Assembly)
                .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter()));

            application = builder.Build();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapControllers();
            application.StartAsync().GetAwaiter().GetResult();
            Client = application.GetTestClient();
        }

        public HttpClient Client { get; }
        public StubSubscriptionService Subscription { get; }
        public StubPaymentService Payment { get; }
        public StubPaymentWebhookService Webhook { get; }

        public void Dispose()
        {
            Client.Dispose();
            application.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        public const string SchemeName = "ContractTest";
        public const string UserIdHeader = "X-Test-User-Id";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserIdHeader, out var userId)
                || !Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim("sub", userId.ToString()),
                new Claim(ClaimTypes.Role, role.ToString())
            };
            var identity = new ClaimsIdentity(
                claims,
                SchemeName,
                ClaimTypes.Name,
                ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class StubSubscriptionService : ISubscriptionService
    {
        public SubscriptionMeResponse? MeResponse { get; set; } = new(
            "Free",
            null,
            new SubscriptionUsageResponse(
                0,
                1,
                new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc)),
            new SubscriptionSavedTripsResponse(0, 1));

        public IReadOnlyList<SubscriptionPlanResponse> GetPlans() =>
        [
            new("Free", 0m, null, 1, 1),
            new("TripPass", 49000m, 7, null, 3),
            new("Membership", 59000m, 30, null, null)
        ];

        public Task<SubscriptionMeResponse?> GetMySubscriptionAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MeResponse);
    }

    private sealed class StubPaymentService : IPaymentService
    {
        public PaymentIntentResult CheckoutResult { get; set; } = new(
            PaymentIntentResultStatus.GatewayUnavailable);

        public PaymentIntentResult RenewResult { get; set; } = new(
            PaymentIntentResultStatus.GatewayUnavailable);

        public PaymentOrderLookupResult OrderResult { get; set; } = new(
            PaymentOrderLookupStatus.NotFound);

        public Task<PaymentIntentResult> CheckoutAsync(
            Guid userId,
            string? planCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CheckoutResult);

        public Task<PaymentIntentResult> RenewAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RenewResult);

        public Task<PaymentOrderLookupResult> GetOrderAsync(
            Guid userId,
            Guid orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OrderResult);
    }

    private sealed class StubPaymentWebhookService : IPaymentWebhookService
    {
        public PaymentWebhookResult Result { get; set; } = new(
            PaymentWebhookStatus.Acknowledged);

        public Task<PaymentWebhookResult> ProcessAsync(
            string rawPayload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }
}
