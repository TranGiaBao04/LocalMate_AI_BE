using System.Net;
using System.Text;
using System.Text.Json;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Payments;
using Microsoft.Extensions.Logging;
using PayOS;
using PayOS.Crypto;

namespace LocalMateAI.Tests;

public sealed class PaymentGatewayReleaseEvidenceTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
    private static readonly UpgradeReleaseCandidate Order = new(Guid.NewGuid(), Guid.NewGuid(), 1234,
        PaymentOrderStatus.Failed, 10000, 49000, null, Now, Now, null, null, []);

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Paid)]
    [InlineData(PaymentGatewayOrderStatus.Underpaid)]
    [InlineData(PaymentGatewayOrderStatus.Pending)]
    [InlineData(PaymentGatewayOrderStatus.Processing)]
    [InlineData(PaymentGatewayOrderStatus.Unknown)]
    [InlineData(PaymentGatewayOrderStatus.Expired)]
    [InlineData(PaymentGatewayOrderStatus.Failed)]
    public void UnprovenTerminalStatesNeverRelease(PaymentGatewayOrderStatus status) =>
        Assert.Null(UpgradeReleasePolicy.Assess(Order, new(true, 1234, 10000, status)
        { RequestedAmount = 10000, AmountPaid = 0, AmountRemaining = 10000 }, Now));

    [Theory]
    [InlineData("missing_paid")]
    [InlineData("missing_remaining")]
    [InlineData("missing_amount")]
    [InlineData("underpaid")]
    [InlineData("remaining")]
    [InlineData("amount")]
    [InlineData("code")]
    [InlineData("unavailable")]
    public void CancelledStillRequiresCompleteFinancialProof(string flaw)
    {
        var p = new PaymentGatewayOrderResult(flaw != "unavailable", flaw == "code" ? 99 : 1234, 10000,
            PaymentGatewayOrderStatus.Cancelled)
        {
            RequestedAmount = flaw == "missing_amount" ? null : flaw == "amount" ? 9999 : 10000,
            AmountPaid = flaw == "missing_paid" ? null : flaw == "underpaid" ? 1 : 0,
            AmountRemaining = flaw == "missing_remaining" ? null : flaw == "remaining" ? 9999 : 10000
        };
        Assert.Null(UpgradeReleasePolicy.Assess(Order, p, Now));
    }

    [Fact]
    public void CancelledZeroFundsProofIsFreshAndOneWay()
    {
        var proof = UpgradeReleasePolicy.Assess(Order, new(true, 1234, 10000, PaymentGatewayOrderStatus.Cancelled)
        { RequestedAmount = 10000, AmountPaid = 0, AmountRemaining = 10000 }, Now)!;
        Assert.True(proof.IsValid(Now)); Assert.False(proof.IsValid(Now.AddMinutes(2)));
        Assert.False(proof.IsValid(Now.AddTicks(-1)));
        var row = new PaymentOrderCredit(); row.Release(Now, proof);
        Assert.True(row.HasValidReleaseEvidence());
        Assert.Throws<InvalidOperationException>(() => row.Release(Now, proof));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("amountPaid")]
    [InlineData("amountRemaining")]
    [InlineData("amount")]
    [InlineData("status")]
    [InlineData("unknown_status")]
    [InlineData("invalid_signature")]
    public async Task OfficialSdkLookupPreservesFieldPresenceAndResponseVerification(string missing)
    {
        var data = new Dictionary<string, object>
        { ["id"] = "controlled", ["orderCode"] = 1234L, ["amount"] = 10000L, ["amountPaid"] = 0L,
          ["amountRemaining"] = 10000L, ["status"] = "CANCELLED", ["createdAt"] = "2026-10-03T00:00:00Z", ["transactions"] = new object[0] };
        if (missing == "unknown_status") data["status"] = "FUTURE_UNKNOWN";
        else data.Remove(missing);
        var signature = missing == "invalid_signature" ? "bad" : new CryptoProvider().CreateSignatureFromObject(data, "checksum-test");
        using var http = new HttpClient(new Handler(JsonSerializer.Serialize(new { code = "00", desc = "success", data, signature })));
        using var sdk = new PayOSClient(new PayOSOptions
        { ClientId = "client-test", ApiKey = "api-test", ChecksumKey = "checksum-test", BaseUrl = "https://payos.test",
          MaxRetries = 0, HttpClient = http, LogLevel = LogLevel.None });
        var result = await new PayOSPaymentGateway(sdk, new()).GetPaymentAsync(1234);
        if (missing is "amount" or "status" or "unknown_status" or "invalid_signature") Assert.False(result.IsAvailable);
        else
        {
            Assert.True(result.IsAvailable);
            Assert.Equal(missing == "amountPaid" ? null : (decimal?)0, result.AmountPaid);
            Assert.Equal(missing == "amountRemaining" ? null : (decimal?)10000, result.AmountRemaining);
            Assert.Equal(missing == "none", UpgradeReleasePolicy.Assess(Order, result, Now) is not null);
        }
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
