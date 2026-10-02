using System.Net;
using System.Net.Http.Json;
using System.Text;
using LocalMateAI.Application.DTOs.ItineraryPurchases;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Tests;

public sealed class SingleItineraryHttpTests
{
    [Theory]
    [InlineData("me")]
    [InlineData("availability")]
    [InlineData("orders/00000000-0000-0000-0000-000000000001")]
    public async Task RegisteredUserAndAccountAccess(string route)
    {
        using var h = new AdminTransactionsHttpTests.Host(purchases: new Service());
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/itinerary-purchases/"+route)).StatusCode);
        h.Authenticate("locked");
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/itinerary-purchases/"+route)).StatusCode);
        h.Authenticate("none");
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/itinerary-purchases/"+route)).StatusCode);
    }
    [Theory]
    [InlineData("amount")]
    [InlineData("userId")]
    [InlineData("productKind")]
    [InlineData("version")]
    [InlineData("durationDays")]
    public async Task UnknownFinancialFields_ActuallyRejectedAtHttpBinding(string key)
    {
        var svc = new Service(); using var h = new AdminTransactionsHttpTests.Host(purchases: svc); h.Authenticate("none");
        var body = "{\"clientAttemptId\":\""+Guid.NewGuid()+"\",\""+key+"\":1}";
        var response = await h.Client.PostAsync("/api/itinerary-purchases/checkout", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, svc.Calls);
    }
    [Fact]
    public async Task Checkout_RequiresAuthAndAttempt_ThereIsNoRenewalRoute()
    {
        var svc = new Service(); using var h = new AdminTransactionsHttpTests.Host(purchases: svc);
        var payload = new { clientAttemptId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsJsonAsync("/api/itinerary-purchases/checkout", payload)).StatusCode);
        h.Authenticate("none");
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsJsonAsync("/api/itinerary-purchases/checkout", new { clientAttemptId = Guid.Empty })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PostAsJsonAsync("/api/itinerary-purchases/checkout", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsJsonAsync("/api/itinerary-purchases/renew", new {})).StatusCode);
    }
    private sealed class Service : IItineraryPurchaseService
    {
        public int Calls;
        private static ItineraryPurchaseOrderResponse Row(Guid id) => new(id,"SingleItinerary","SINGLE_ITINERARY",
            Guid.NewGuid(),"Pending",29000,"VND",SingleItineraryPostgresTests.Now,null,"https://example.invalid","qr",null);
        public Task<ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>> CheckoutAsync(Guid user, Guid attempt, CancellationToken ct = default)
        { Calls++; return Task.FromResult(new ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>(attempt == Guid.Empty ? ItineraryPurchaseStatus.InvalidRequest : ItineraryPurchaseStatus.Success, Row(attempt))); }
        public Task<ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>> GetOrderAsync(Guid user, Guid id, CancellationToken ct = default) =>
            Task.FromResult(new ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>(ItineraryPurchaseStatus.Success,Row(id)));
        public Task<ItineraryPurchaseResult<ItineraryPurchasesMeResponse>> GetMeAsync(Guid user, CancellationToken ct = default) =>
            Task.FromResult(new ItineraryPurchaseResult<ItineraryPurchasesMeResponse>(ItineraryPurchaseStatus.Success,new(0,[])));
        public Task<ItineraryPurchaseResult<ItineraryPurchaseAvailabilityResponse>> GetAvailabilityAsync(Guid user, CancellationToken ct = default) =>
            Task.FromResult(new ItineraryPurchaseResult<ItineraryPurchaseAvailabilityResponse>(ItineraryPurchaseStatus.Success,
                new("SingleItinerary","SINGLE_ITINERARY","Single",Guid.NewGuid(),29000,"VND",true,null,0,true,0,1)));
    }
}
