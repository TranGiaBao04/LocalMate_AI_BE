using System.Net;
using System.Text.Json;
using LocalMateAI.Application.DTOs.Payments;

namespace LocalMateAI.Tests;

public sealed class AdminTransactionDetailHttpTests
{
    private static string Path => "/api/admin/transactions/" + AdminTransactionTests.Row.Id;
    private static AdminTransactionsHttpTests.Host Host(AdminTransactionDetailResponse? detail) =>
        new(AdminTransactionTests.Service(new AdminTransactionTests.RecordingRepository { Detail = detail }));

    [Fact]
    public async Task Anonymous_Is401()
    {
        using var host = Host(AdminTransactionDetailTests.Detail);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(Path)).StatusCode);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("manage")]
    [InlineData("demo")]
    public async Task WithoutViewRevenue_Is403(string mode)
    {
        using var host = Host(AdminTransactionDetailTests.Detail);
        host.Authenticate(mode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(Path)).StatusCode);
    }

    [Theory]
    [InlineData("locked", 403, "account_locked")]
    [InlineData("missing", 401, "account_not_found")]
    public async Task AccountAccessBehavior_IsUnchanged(string mode, int status, string code)
    {
        using var host = Host(AdminTransactionDetailTests.Detail);
        host.Authenticate(mode);
        var response = await host.Client.GetAsync(Path);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ViewRevenue_WithoutAdminRole_ReturnsExactSafeSchema()
    {
        using var host = Host(AdminTransactionDetailTests.Detail);
        host.Authenticate();
        var response = await host.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(new[] { "creditSources", "entitlement", "repairEligibility", "repairHistory", "singleItineraryEntitlement", "statusHistory", "transaction", "webhookReceipts" },
            root.EnumerateObject().Select(p => p.Name).Order());
        var transaction = root.GetProperty("transaction");
        Assert.Equal("SubscriptionPlan", transaction.GetProperty("productKind").GetString());
        Assert.Equal("Native", transaction.GetProperty("planVersionBinding").GetString());
        Assert.Equal(AdminTransactionTests.Row.Id, transaction.GetProperty("id").GetGuid());
        Assert.Equal(new[] { "actorUserId", "fromStatus", "id", "occurredAt", "reasonCode", "source", "toStatus", "webhookReceiptId" },
            root.GetProperty("statusHistory")[0].EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(new[] { "amount", "hasRawPayload", "id", "isSuccessful", "providerOrderCode", "rawPayloadPurgedAt",
                "rawPayloadRetainUntil", "rawPayloadSha256", "receivedAt" },
            root.GetProperty("webhookReceipts")[0].EnumerateObject().Select(p => p.Name).Order());
        AssertSafe(root);
    }

    [Fact]
    public async Task UnknownGuid_IsStable404_WithoutRelatedEntityDetails()
    {
        using var host = Host(null);
        host.Authenticate();
        var response = await host.Client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("transaction_not_found", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("transaction", out _));
        Assert.False(json.RootElement.TryGetProperty("webhookReceipts", out _));
    }

    [Fact]
    public async Task LegacyNullIdentityAndMissingHistory_AreNotFabricated()
    {
        var row = AdminTransactionTests.Row with { PlanCode = "TripPass", PlanName = null, Amount = 49000m };
        using var host = Host(new(new(row, null, null, "LegacyUnresolved", null), [], []));
        host.Authenticate();
        using var json = JsonDocument.Parse(await (await host.Client.GetAsync(Path)).Content.ReadAsStringAsync());
        var root = json.RootElement;
        var transaction = root.GetProperty("transaction");
        Assert.Equal("TripPass", transaction.GetProperty("planCode").GetString());
        foreach (var name in new[] { "planId", "planVersionId", "planName", "updatedAt" })
            Assert.Equal(JsonValueKind.Null, transaction.GetProperty(name).ValueKind);
        Assert.Equal(49000m, transaction.GetProperty("amount").GetDecimal());
        Assert.Equal(0, root.GetProperty("statusHistory").GetArrayLength());
        Assert.Equal(0, root.GetProperty("webhookReceipts").GetArrayLength());
    }

    internal static void AssertSafe(JsonElement element)
    {
        string[] forbidden = ["rawPayload", "checkoutUrl", "qrCode", "authorization", "cookies", "headers",
            "passwordHash", "accessToken", "jwt", "clientSecret", "checksumKey", "bankAccount"];
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(property.Name, forbidden, StringComparer.OrdinalIgnoreCase);
                AssertSafe(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) AssertSafe(item);
    }
}
