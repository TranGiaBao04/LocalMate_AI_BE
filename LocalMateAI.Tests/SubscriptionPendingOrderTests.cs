using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class SubscriptionPendingOrderTests
{
    private static readonly DateTime Now = SubscriptionCheckoutQuoteTests.Now;
    private static readonly Guid User = SubscriptionCheckoutQuoteTests.UserId;
    private static PaymentOrder Pending(PlanCode plan, PaymentOrderType type) => new()
    {
        UserId = User, PlanId = SubscriptionBaseline.PlanId(plan), Type = type,
        Amount = 19000, ExpiresAt = Now.AddMinutes(5), CheckoutUrl = "https://checkout.test/owned", QrCode = "owned-qr"
    };

    [Theory]
    [InlineData(PlanCode.TripPass, PaymentOrderType.Purchase, PaymentIntentResultStatus.AnotherPendingOrder)]
    [InlineData(PlanCode.Membership, PaymentOrderType.Renewal, PaymentIntentResultStatus.AnotherPendingOrder)]
    [InlineData(PlanCode.Membership, PaymentOrderType.Purchase, PaymentIntentResultStatus.PendingOrderExists)]
    public async Task GlobalPendingPreventsNewCharge(PlanCode plan, PaymentOrderType type, PaymentIntentResultStatus expected)
    {
        var f = new PaymentServiceTests.Fixture(now: Now, existingOrders: [Pending(plan, type)]);
        Assert.Equal(expected, (await f.Service.CheckoutAsync(User, "Membership")).Status);
        Assert.Single(f.Orders.Items); Assert.Empty(f.Gateway.Requests); Assert.Equal(0, f.Gateway.LookupCalls);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Expired)]
    public async Task TerminalUpgradeClaimsBlockButOrdinaryTerminalOrdersDoNot(PaymentOrderStatus status)
    {
        var o = Pending(PlanCode.TripPass, PaymentOrderType.Upgrade); o.Status = status;
        var f = new PaymentServiceTests.Fixture(now: Now, existingOrders: [o]);
        f.Orders.ReservedOrders.Add(o.Id);
        Assert.Equal(PaymentIntentResultStatus.AnotherPendingOrder, (await f.Service.CheckoutAsync(User, "Membership")).Status);
        f.Orders.ReservedOrders.Clear();
        Assert.Equal(PaymentIntentResultStatus.Success, (await f.Service.CheckoutAsync(User, "Membership")).Status);
    }

    [Fact]
    public async Task SinglePendingIsIndependent()
    {
        var single = Pending(PlanCode.TripPass, PaymentOrderType.Purchase);
        single.ProductKind = PaymentProductKind.SingleItinerary; single.PlanId = null;
        var f = new PaymentServiceTests.Fixture(now: Now, existingOrders: [single]);
        Assert.Equal(PaymentIntentResultStatus.Success, (await f.Service.CheckoutAsync(User, "Membership")).Status);
        Assert.Equal(2, f.Orders.Items.Count);
    }

    [Fact]
    public async Task ExpiredOrdinaryPendingLocallyExpiresThenReplacementUsesFullPrice()
    {
        var old = Pending(PlanCode.TripPass, PaymentOrderType.Renewal); old.ExpiresAt = Now;
        var f = new PaymentServiceTests.Fixture(now: Now, existingOrders: [old]);
        var result = await f.Service.CheckoutAsync(User, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        Assert.Equal(59000, result.Response!.Amount); Assert.Equal(0, result.Response.CreditAmount);
        Assert.Equal(PaymentOrderStatus.Expired, old.Status); Assert.Single(f.Orders.Histories);
    }

    [Theory]
    [InlineData(PaymentOrderType.Purchase, "another_pending_order")]
    [InlineData(PaymentOrderType.Upgrade, "pending_order_exists")]
    public async Task HttpUpgradeReportsActualBlockerBeforePhaseGate_QuoteStillIgnoresIt(PaymentOrderType type, string code)
    {
        var pending = Pending(PlanCode.Membership, type);
        var f = new PaymentServiceTests.Fixture(now: Now, existingOrders: [pending]);
        SubscriptionCheckoutQuoteTests.AddNative(f);
        using var host = new SubscriptionHttpContractTests.ContractTestHost(f.Service);
        host.Client.DefaultRequestHeaders.Add("X-Test-User-Id", User.ToString());
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/subscription/checkout-quote?planCode=Membership")).StatusCode);
        var response = await host.Client.PostAsJsonAsync("/api/subscription/checkout", new { planCode = "Membership" });
        await SubscriptionHttpContractTests.AssertProblemAsync(response, HttpStatusCode.Conflict, code);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(pending.Id, json.RootElement.GetProperty("orderId").GetGuid());
        Assert.DoesNotContain("periodId", json.RootElement.GetRawText());
        Assert.Empty(f.Gateway.Requests); Assert.Equal(0, f.Gateway.LookupCalls); Assert.Single(f.Orders.Items);
    }
}
