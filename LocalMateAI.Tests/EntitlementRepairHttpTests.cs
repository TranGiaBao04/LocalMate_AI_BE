using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class EntitlementRepairHttpTests
{
    internal static readonly Guid OrderId = Guid.NewGuid();
    private static string Path => $"/api/admin/transactions/{OrderId}/repair-entitlement";

    [Fact]
    public async Task ManagePlansOnly_ExistingAccountMiddleware_NoViewRevenueAndRequirement()
    {
        var service = new RecordingService();
        using var host = new AdminTransactionsHttpTests.Host(repair: service);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync(Path, new { reason = "restore" })).StatusCode);
        foreach (var mode in new[] { "revenue", "none", "demo", "locked" })
        {
            host.Authenticate(mode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsJsonAsync(Path, new { reason = "restore" })).StatusCode);
        }
        Assert.Equal(0, service.Calls);
        host.Authenticate("manage");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync(Path, new { reason = "restore" })).StatusCode);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000003"), service.Actor);
        Assert.Equal(1, service.Calls);
    }

    [Theory]
    [InlineData(EntitlementRepairOutcome.Repaired)]
    [InlineData(EntitlementRepairOutcome.AlreadyGranted)]
    public async Task Success_ExactSafeSchema(EntitlementRepairOutcome outcome)
    {
        using var host = new AdminTransactionsHttpTests.Host(repair: new RecordingService(outcome));
        host.Authenticate("manage");
        var response = await host.Client.PostAsJsonAsync(Path, new { reason = "Khôi phục quyền lịch sử" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "auditId", "decisionCode", "endsAt", "orderId", "reconstructionMode", "result", "startsAt", "subscriptionPeriodId" },
            json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(outcome.ToString(), json.RootElement.GetProperty("result").GetString());
        AdminTransactionDetailHttpTests.AssertSafe(json.RootElement);
    }

    [Theory]
    [InlineData(EntitlementRepairOutcome.NotEligible, "entitlement_repair_not_eligible")]
    [InlineData(EntitlementRepairOutcome.Conflict, "entitlement_repair_conflict")]
    public async Task ExpectedRejection_409WithSafeDecision(EntitlementRepairOutcome outcome, string code)
    {
        using var host = new AdminTransactionsHttpTests.Host(repair: new RecordingService(outcome));
        host.Authenticate("manage");
        var response = await host.Client.PostAsJsonAsync(Path, new { reason = "restore" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(outcome.ToString(), json.RootElement.GetProperty("result").GetString());
        Assert.Equal("historical_window_unproven", json.RootElement.GetProperty("decisionCode").GetString());
        Assert.Equal(OrderId, json.RootElement.GetProperty("orderId").GetGuid());
        Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("auditId").GetGuid());
        AdminTransactionDetailHttpTests.AssertSafe(json.RootElement);
    }

    [Fact]
    public async Task NotFound_Is404_NoFakeAudit()
    {
        using var host = new AdminTransactionsHttpTests.Host(repair: new RecordingService { Missing = true });
        host.Authenticate("manage");
        var response = await host.Client.PostAsJsonAsync(Path, new { reason = "restore" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("transaction_not_found", json.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"reason\":null}")]
    [InlineData("{\"reason\":\" \"}")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"reason\":\"restore\",\"startsAt\":\"2026-01-01\"}")]
    [InlineData("{\"reason\":\"restore\",\"userId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"reason\":\"restore\",\"amount\":19000}")]
    public async Task InvalidOrExtraBodyFields_Are400StableCode(string body)
    {
        var service = new RecordingService();
        using var host = new AdminTransactionsHttpTests.Host(repair: service);
        host.Authenticate("manage");
        var response = await host.Client.PostAsync(Path, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_entitlement_repair_request", json.RootElement.GetProperty("code").GetString());
        AdminTransactionDetailHttpTests.AssertSafe(json.RootElement);
    }

    internal sealed class RecordingService(EntitlementRepairOutcome outcome = EntitlementRepairOutcome.Repaired) : IEntitlementRepairService
    {
        internal bool Missing; internal int Calls; internal Guid Actor;
        public Task<EntitlementRepairServiceResult> RepairAsync(Guid id, Guid actor, string? reason, CancellationToken ct = default)
        {
            if (!EntitlementRepairReason.TryNormalize(reason, out _)) return Task.FromResult(new EntitlementRepairServiceResult(true));
            Calls++; Actor = actor;
            return Task.FromResult(Missing ? new EntitlementRepairServiceResult(false) : new(false, new(outcome, id,
                Guid.NewGuid(), EntitlementRepairTests.Day, EntitlementRepairTests.Day.AddDays(7), Guid.NewGuid(),
                outcome is EntitlementRepairOutcome.NotEligible or EntitlementRepairOutcome.Conflict ? "historical_window_unproven" : "already_granted",
                "DeterministicHistoricalReplay")));
        }
    }
}
