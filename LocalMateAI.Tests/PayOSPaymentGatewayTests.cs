using System.Net;
using System.Text;
using System.Text.Json;
using LocalMateAI.Application.Payments;
using LocalMateAI.Infrastructure.Payments;
using Microsoft.Extensions.Logging;
using PayOS;
using PayOS.Crypto;
using PayOS.Models.Webhooks;

namespace LocalMateAI.Tests;

public sealed class PayOSPaymentGatewayTests
{
    private const string ClientId = "controlled-client-id";
    private const string ApiKey = "controlled-api-key";
    private const string ChecksumKey = "controlled-test-checksum-key";

    [Fact]
    public async Task CreatePaymentLinkAsync_MapsRequestAndProviderResponseThroughOfficialSdk()
    {
        const long providerOrderCode = 8123456789;
        const long amount = 59000;
        var expiresAt = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var handler = new CapturingHttpMessageHandler(() => ProviderResponse(
            providerOrderCode,
            amount,
            "https://checkout.test/payment",
            "controlled-qr-code"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var options = Options();
        var gateway = new PayOSPaymentGateway(client, options);

        var result = await gateway.CreatePaymentLinkAsync(new PaymentLinkRequest(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            providerOrderCode,
            "Membership",
            LocalMateAI.Domain.Enums.PaymentOrderType.Purchase,
            amount,
            expiresAt));

        Assert.True(result.IsSuccess);
        Assert.Equal("https://checkout.test/payment", result.CheckoutUrl);
        Assert.Equal("controlled-qr-code", result.QrCode);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v2/payment-requests", handler.RequestUri?.AbsolutePath);
        Assert.Equal(ClientId, handler.Header("x-client-id"));
        Assert.Equal(ApiKey, handler.Header("x-api-key"));

        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal(providerOrderCode, root.GetProperty("orderCode").GetInt64());
        Assert.Equal(amount, root.GetProperty("amount").GetInt64());
        Assert.Equal("LM3456789", root.GetProperty("description").GetString());
        Assert.Equal(options.ReturnUrl, root.GetProperty("returnUrl").GetString());
        Assert.Equal(options.CancelUrl, root.GetProperty("cancelUrl").GetString());
        Assert.Equal(
            new DateTimeOffset(expiresAt).ToUnixTimeSeconds(),
            root.GetProperty("expiredAt").GetInt64());

        var signature = root.GetProperty("signature").GetString();
        Assert.False(string.IsNullOrWhiteSpace(signature));
        var expectedSignature = client.Crypto.CreateSignatureOfPaymentRequest(
            new
            {
                orderCode = providerOrderCode,
                amount,
                description = "LM3456789",
                returnUrl = options.ReturnUrl,
                cancelUrl = options.CancelUrl
            },
            ChecksumKey);
        Assert.Equal(expectedSignature, signature);
    }

    [Theory]
    [InlineData(8123456790, 59000, "https://checkout.test/payment", "controlled-qr-code")]
    [InlineData(8123456789, 59001, "https://checkout.test/payment", "controlled-qr-code")]
    [InlineData(8123456789, 59000, "", "controlled-qr-code")]
    [InlineData(8123456789, 59000, "https://checkout.test/payment", "")]
    public async Task CreatePaymentLinkAsync_UntrustedProviderResponse_ReturnsUnavailable(
        long responseOrderCode,
        long responseAmount,
        string checkoutUrl,
        string qrCode)
    {
        var handler = new CapturingHttpMessageHandler(() => ProviderResponse(
            responseOrderCode,
            responseAmount,
            checkoutUrl,
            qrCode));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var gateway = new PayOSPaymentGateway(client, Options());

        var result = await gateway.CreatePaymentLinkAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.Null(result.CheckoutUrl);
        Assert.Null(result.QrCode);
    }

    [Theory]
    [InlineData(0, 59000, 2030, 1, 2)]
    [InlineData(8123456789, 0, 2030, 1, 2)]
    [InlineData(8123456789, 59000.5, 2030, 1, 2)]
    [InlineData(8123456789, 59000, 2040, 1, 2)]
    public async Task CreatePaymentLinkAsync_UnrepresentableInput_IsRejectedBeforeTransport(
        long providerOrderCode,
        decimal amount,
        int year,
        int month,
        int day)
    {
        var handler = new CapturingHttpMessageHandler(() => ProviderResponse(
            providerOrderCode,
            decimal.ToInt64(decimal.Truncate(amount)),
            "https://checkout.test/payment",
            "controlled-qr-code"));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var gateway = new PayOSPaymentGateway(client, Options());
        var request = Request(
            providerOrderCode,
            amount,
            new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(
            () => gateway.CreatePaymentLinkAsync(request));
        Assert.Null(handler.Method);
    }

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

    private static PayOSClient CreateClient(HttpClient? httpClient = null) => new(new PayOSOptions
    {
        ClientId = ClientId,
        ApiKey = ApiKey,
        ChecksumKey = ChecksumKey,
        BaseUrl = "https://payos.test",
        MaxRetries = 0,
        LogLevel = LogLevel.None,
        HttpClient = httpClient
    });

    private static PayOSGatewayOptions Options(
        string returnUrl = "http://localhost:5173/payment/success") => new()
    {
        ClientId = ClientId,
        ApiKey = ApiKey,
        ChecksumKey = ChecksumKey,
        ReturnUrl = returnUrl,
        CancelUrl = "http://localhost:5173/payment/cancel"
    };

    private static PaymentLinkRequest Request(
        long providerOrderCode = 8123456789,
        decimal amount = 59000m,
        DateTime? expiresAt = null) => new(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            providerOrderCode,
            "Membership",
            LocalMateAI.Domain.Enums.PaymentOrderType.Purchase,
            amount,
            expiresAt ?? new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc));

    private static HttpResponseMessage ProviderResponse(
        long orderCode,
        long amount,
        string checkoutUrl,
        string qrCode)
    {
        var data = new PayOS.Models.V2.PaymentRequests.CreatePaymentLinkResponse
        {
            Bin = "970400",
            AccountNumber = "123456789",
            AccountName = "LOCALMATE TEST",
            Amount = amount,
            Description = $"LM{orderCode % 10_000_000:D7}",
            OrderCode = orderCode,
            Currency = "VND",
            PaymentLinkId = "controlled-payment-link-id",
            Status = PayOS.Models.V2.PaymentRequests.PaymentLinkStatus.Pending,
            CheckoutUrl = checkoutUrl,
            QrCode = qrCode
        };
        var body = JsonSerializer.Serialize(new
        {
            code = "00",
            desc = "success",
            data,
            signature = new CryptoProvider().CreateSignatureFromObject(data, ChecksumKey)
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CapturingHttpMessageHandler(
        Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public IReadOnlyDictionary<string, string[]> Headers { get; private set; } =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public string? Header(string name) =>
            Headers.TryGetValue(name, out var values) ? Assert.Single(values) : null;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.AsEnumerable();
            if (request.Content is not null)
            {
                headers = headers.Concat(request.Content.Headers);
            }

            Headers = headers
                .ToDictionary(
                    header => header.Key,
                    header => header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            return responseFactory();
        }
    }

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
