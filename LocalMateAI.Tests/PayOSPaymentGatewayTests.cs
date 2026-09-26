using System.Text.Json;
using LocalMateAI.Infrastructure.Payments;
using Microsoft.Extensions.Logging;
using PayOS;
using PayOS.Models.Webhooks;

namespace LocalMateAI.Tests;

public sealed class PayOSPaymentGatewayTests
{
    private const string ChecksumKey = "controlled-test-checksum-key";

    [Fact]
    public void Options_RequireCredentialsAndNavigationUrls()
    {
        Assert.False(new PayOSGatewayOptions().IsComplete());
        Assert.False(Options(returnUrl: "not-a-url").IsComplete());
        Assert.True(Options().IsComplete());
    }

    [Fact]
    public async Task VerifyWebhookAsync_UsesOfficialSdkAndReturnsNormalizedData()
    {
        using var client = CreateClient();
        var gateway = new PayOSPaymentGateway(client, Options());
        var webhook = CreateWebhook(client, success: true);

        var result = await gateway.VerifyWebhookAsync(JsonSerializer.Serialize(webhook));

        Assert.True(result.IsValid);
        Assert.NotNull(result.Notification);
        Assert.Equal(8123456789, result.Notification.ProviderOrderCode);
        Assert.Equal(59000, result.Notification.Amount);
        Assert.True(result.Notification.IsSuccessful);
    }

    [Fact]
    public async Task VerifyWebhookAsync_InvalidSignatureIsRejected()
    {
        using var client = CreateClient();
        var gateway = new PayOSPaymentGateway(client, Options());
        var webhook = CreateWebhook(client, success: true);
        webhook.Signature = "invalid-signature";

        var result = await gateway.VerifyWebhookAsync(JsonSerializer.Serialize(webhook));

        Assert.False(result.IsValid);
        Assert.Null(result.Notification);
    }

    [Fact]
    public async Task VerifyWebhookAsync_VerifiedProviderFailureNormalizesAsNonSuccess()
    {
        using var client = CreateClient();
        var gateway = new PayOSPaymentGateway(client, Options());
        var webhook = CreateWebhook(client, success: false);

        var result = await gateway.VerifyWebhookAsync(JsonSerializer.Serialize(webhook));

        Assert.True(result.IsValid);
        Assert.False(result.Notification!.IsSuccessful);
    }

    private static PayOSClient CreateClient() => new(new PayOSOptions
    {
        ClientId = "controlled-client-id",
        ApiKey = "controlled-api-key",
        ChecksumKey = ChecksumKey,
        LogLevel = LogLevel.None
    });

    private static PayOSGatewayOptions Options(
        string returnUrl = "http://localhost:5173/payment/success") => new()
    {
        ClientId = "controlled-client-id",
        ApiKey = "controlled-api-key",
        ChecksumKey = ChecksumKey,
        ReturnUrl = returnUrl,
        CancelUrl = "http://localhost:5173/payment/cancel"
    };

    private static Webhook CreateWebhook(PayOSClient client, bool success)
    {
        var data = new WebhookData
        {
            OrderCode = 8123456789,
            Amount = 59000,
            Description = "LM3456789",
            AccountNumber = "12345678",
            Reference = "CONTROLLED-REFERENCE",
            TransactionDateTime = "2026-09-26 12:00:00",
            Currency = "VND",
            PaymentLinkId = "controlled-payment-link-id",
            Code = "00",
            Description2 = success ? "Success" : "Failed",
            CounterAccountBankId = string.Empty,
            CounterAccountBankName = string.Empty,
            CounterAccountName = string.Empty,
            CounterAccountNumber = string.Empty,
            VirtualAccountName = string.Empty,
            VirtualAccountNumber = string.Empty
        };
        return new Webhook
        {
            Code = success ? "00" : "01",
            Description = success ? "success" : "failed",
            Success = success,
            Data = data,
            Signature = client.Crypto.CreateSignatureFromObject(data, ChecksumKey)
                        ?? throw new InvalidOperationException("SDK did not create a signature.")
        };
    }
}
