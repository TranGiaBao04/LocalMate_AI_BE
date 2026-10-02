using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class SubscriptionCheckoutQuoteHttpTests
{
    [Theory]
    [InlineData(PaymentIntentResultStatus.InvalidPlanCode, 400, "invalid_plan_code")]
    [InlineData(PaymentIntentResultStatus.PlanAlreadyActive, 409, "plan_already_active")]
    [InlineData(PaymentIntentResultStatus.CoveredByHigherPlan, 409, "already_covered_by_higher_plan")]
    [InlineData(PaymentIntentResultStatus.TargetPlanAlreadyScheduled, 409, "target_plan_already_scheduled")]
    [InlineData(PaymentIntentResultStatus.NonPersistedUser, 403, "persisted_account_required")]
    public async Task QuoteErrorsAreSafeProblems(PaymentIntentResultStatus status, int http, string code)
    {
        using var host = new SubscriptionHttpContractTests.ContractTestHost();
        host.Payment.QuoteResult = new(status);
        SubscriptionHttpContractTests.Authenticate(host.Client);
        await SubscriptionHttpContractTests.AssertProblemAsync(
            await host.Client.GetAsync("/api/subscription/checkout-quote?planCode=Membership"), (HttpStatusCode)http, code);
    }

    [Theory]
    [InlineData("bad-subject")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidIdentityRejected(string subject)
    {
        using var host = new SubscriptionHttpContractTests.ContractTestHost();
        host.Client.DefaultRequestHeaders.Add("X-Test-User-Id", subject);
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        await SubscriptionHttpContractTests.AssertProblemAsync(
            await host.Client.GetAsync("/api/subscription/checkout-quote?planCode=Membership"),
            HttpStatusCode.Unauthorized, "invalid_identity");
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("creditAmount")]
    [InlineData("userId")]
    [InlineData("versionId")]
    [InlineData("periodId")]
    [InlineData("type")]
    public async Task CheckoutActuallyRejectsClientFinancialFields(string field)
    {
        var f = new PaymentServiceTests.Fixture();
        using var host = new SubscriptionHttpContractTests.ContractTestHost(f.Service);
        SubscriptionHttpContractTests.Authenticate(host.Client);
        var response = await host.Client.PostAsJsonAsync("/api/subscription/checkout",
            new Dictionary<string, object> { ["planCode"] = "Membership", [field] = "controlled" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(f.Orders.Items); Assert.Empty(f.Gateway.Requests);
    }

    [Fact]
    public async Task UpgradeQuoteAndSafetyCheckoutUseRealSharedPolicy_ExposeOnlySafeQuote()
    {
        var f = new PaymentServiceTests.Fixture(now: SubscriptionCheckoutQuoteTests.Now);
        SubscriptionCheckoutQuoteTests.AddNative(f);
        using var host = new SubscriptionHttpContractTests.ContractTestHost(f.Service);
        host.Client.DefaultRequestHeaders.Add("X-Test-User-Id", SubscriptionCheckoutQuoteTests.UserId.ToString());
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        var response = await host.Client.GetAsync("/api/subscription/checkout-quote?planCode=Membership");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(new[] { "amount", "creditAmount", "credits", "durationDays", "listPrice", "planCode", "type" },
            root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("Upgrade", root.GetProperty("type").GetString());
        Assert.Equal(49000, root.GetProperty("amount").GetDecimal());
        var row = Assert.Single(root.GetProperty("credits").EnumerateArray());
        Assert.Equal(new[] { "creditAmount", "planCode", "planName", "remainingDays" },
            row.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var checkout = await host.Client.PostAsJsonAsync("/api/subscription/checkout", new { planCode = "Membership" });
        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
        using var intent = JsonDocument.Parse(await checkout.Content.ReadAsStringAsync());
        Assert.Equal("Upgrade", intent.RootElement.GetProperty("type").GetString());
        Assert.Equal(49000, intent.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(59000, intent.RootElement.GetProperty("listPrice").GetDecimal());
        Assert.Single(f.Orders.Items); Assert.Single(f.Gateway.Requests);
        await SubscriptionHttpContractTests.AssertProblemAsync(
            await host.Client.PostAsJsonAsync("/api/subscription/checkout", new { planCode = "Membership" }),
            HttpStatusCode.Conflict, "pending_order_exists");
        Assert.Single(f.Gateway.Requests);
        Assert.Equal(0, f.Gateway.LookupCalls);
    }

    [Fact]
    public async Task ReviewRequiredIs409ForCheckoutAndRenewal_OwnerMetadataOnly()
    {
        var f = new PaymentServiceTests.Fixture(now: SubscriptionCheckoutQuoteTests.Now);
        SubscriptionCheckoutQuoteTests.AddNative(f);
        var result = await f.Service.CheckoutAsync(SubscriptionCheckoutQuoteTests.UserId, "Membership");
        var order = Assert.Single(f.Orders.Items);
        order.Status = PaymentOrderStatus.ReviewRequired;
        using var host = new SubscriptionHttpContractTests.ContractTestHost(f.Service);
        host.Client.DefaultRequestHeaders.Add("X-Test-User-Id", SubscriptionCheckoutQuoteTests.UserId.ToString());
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        await SubscriptionHttpContractTests.AssertProblemAsync(
            await host.Client.PostAsJsonAsync("/api/subscription/checkout", new { planCode = "TripPass" }),
            HttpStatusCode.Conflict, "payment_review_required");
        await SubscriptionHttpContractTests.AssertProblemAsync(
            await host.Client.PostAsJsonAsync("/api/subscription/renew", new { }),
            HttpStatusCode.Conflict, "payment_review_required");
        Assert.Single(f.Orders.Items); Assert.Single(f.Gateway.Requests); Assert.Equal(0, f.Gateway.LookupCalls);
        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task PurchaseIntentKeepsOldFieldsAndAddsSnapshotMetadata()
    {
        var f = new PaymentServiceTests.Fixture();
        using var host = new SubscriptionHttpContractTests.ContractTestHost(f.Service);
        SubscriptionHttpContractTests.Authenticate(host.Client);
        var response = await host.Client.PostAsJsonAsync("/api/subscription/checkout", new { planCode = " membership " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Purchase", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(59000, json.RootElement.GetProperty("listPrice").GetDecimal());
        Assert.Equal(0, json.RootElement.GetProperty("creditAmount").GetDecimal());
        Assert.True(json.RootElement.TryGetProperty("checkoutUrl", out _));
        Assert.True(json.RootElement.TryGetProperty("qrCode", out _));
    }
}
