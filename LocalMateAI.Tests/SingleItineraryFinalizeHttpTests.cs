using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Tests;

public sealed class SingleItineraryFinalizeHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Finalize_ExplicitSingleOrExistingEmptyBodyWorks(bool single)
    {
        var service = DispatchProxy.Create<ITripService, Stub>(); using var host = new AdminTransactionsHttpTests.Host(trips: service);
        host.Authenticate("none"); var id = Guid.NewGuid();
        var response = single ? await host.Client.PostAsJsonAsync("/api/trips/"+id+"/finalize",
            new { fundingSource = "SingleEntitlement", entitlementId = Guid.NewGuid() }) :
            await host.Client.PostAsync("/api/trips/"+id+"/finalize", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinalizeTripResponse>();
        Assert.Equal(single ? "SingleEntitlement" : "Normal", payload!.FundingSource);
    }
    [Theory]
    [InlineData("{\"fundingSource\":\"SingleEntitlement\"}")]
    [InlineData("{\"fundingSource\":\"Generate\"}")]
    [InlineData("{\"fundingSource\":\"Normal\",\"amount\":1}")]
    [InlineData("{\"fundingSource\":\"Normal\",\"userId\":\"00000000-0000-0000-0000-000000000001\"}")]
    public async Task InvalidOrFinancialFinalizeBody_Is400(string body)
    {
        using var host = new AdminTransactionsHttpTests.Host(trips: DispatchProxy.Create<ITripService, Stub>());
        host.Authenticate("none");
        var response = await host.Client.PostAsync("/api/trips/"+Guid.NewGuid()+"/finalize",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    public class Unused : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new NotSupportedException();
    }
    public class Stub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var values = args ?? throw new InvalidOperationException();
            var request = values.OfType<FinalizeTripRequest>().Single();
            return Task.FromResult(request.IsValid ? FinalizeTripResult.Succeeded(new((Guid)values[1]!, "Finalized")
                { FundingSource = request.FundingSource, EntitlementId = request.EntitlementId })
                : new FinalizeTripResult(FinalizeTripResultStatus.InvalidFunding));
        }
    }
}
