using System.Text.Json;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using PayOS;
using PayOS.Exceptions;
using PayOS.Models;
using PayOS.Models.Webhooks;
using PayOS.Models.V2.PaymentRequests;
using ApplicationPaymentLinkRequest = LocalMateAI.Application.Payments.PaymentLinkRequest;
using SdkPaymentLinkRequest = PayOS.Models.V2.PaymentRequests.CreatePaymentLinkRequest;

namespace LocalMateAI.Infrastructure.Payments;

public sealed class PayOSPaymentGateway(
    PayOSClient client,
    PayOSGatewayOptions options) : IPaymentGateway
{
    private static readonly JsonSerializerOptions WebhookJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
        ApplicationPaymentLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        var amount = ToProviderAmount(request.Amount);
        var expiredAt = ToProviderExpiration(request.ExpiresAt);
        var providerRequest = new SdkPaymentLinkRequest
        {
            OrderCode = request.ProviderOrderCode,
            Amount = amount,
            Description = CreateDescription(request.ProviderOrderCode),
            ReturnUrl = options.ReturnUrl,
            CancelUrl = options.CancelUrl,
            ExpiredAt = expiredAt
        };

        try
        {
            var response = await client.PaymentRequests.CreateAsync(
                providerRequest,
                new RequestOptions<SdkPaymentLinkRequest>
                {
                    CancellationToken = cancellationToken
                });
            if (response.OrderCode != request.ProviderOrderCode
                || response.Amount != amount
                || string.IsNullOrWhiteSpace(response.CheckoutUrl)
                || string.IsNullOrWhiteSpace(response.QrCode))
            {
                return PaymentLinkResult.Unavailable();
            }

            return PaymentLinkResult.Succeeded(response.CheckoutUrl, response.QrCode);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            throw new PaymentGatewayUnavailableException(
                "The PayOS payment-link service is unavailable.");
        }
    }

    public async Task<PaymentGatewayOrderResult> GetPaymentAsync(
        long providerOrderCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await client.PaymentRequests.GetAsync(
                providerOrderCode,
                new RequestOptions { CancellationToken = cancellationToken });
            return new PaymentGatewayOrderResult(
                true,
                payment.OrderCode,
                payment.Status is PaymentLinkStatus.Paid or PaymentLinkStatus.Underpaid
                    ? payment.AmountPaid
                    : payment.Amount,
                MapStatus(payment.Status));
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            return PaymentGatewayOrderResult.Unavailable(providerOrderCode);
        }
    }

    public async Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return PaymentWebhookVerificationResult.Invalid();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var webhook = JsonSerializer.Deserialize<Webhook>(rawPayload, WebhookJsonOptions);
            if (webhook?.Data is null)
            {
                return PaymentWebhookVerificationResult.Invalid();
            }

            var verifiedData = await client.Webhooks.VerifyAsync(webhook);
            cancellationToken.ThrowIfCancellationRequested();
            var isSuccessful = webhook.Success
                               && string.Equals(webhook.Code, "00", StringComparison.Ordinal)
                               && string.Equals(verifiedData.Code, "00", StringComparison.Ordinal);
            return PaymentWebhookVerificationResult.Valid(
                new VerifiedPaymentNotification(
                    verifiedData.OrderCode,
                    verifiedData.Amount,
                    isSuccessful));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
            or WebhookException
            or PayOSException
            or ArgumentException)
        {
            return PaymentWebhookVerificationResult.Invalid();
        }
    }

    private static long ToProviderAmount(decimal amount)
    {
        if (amount <= 0 || amount != decimal.Truncate(amount) || amount > long.MaxValue)
        {
            throw new PaymentGatewayUnavailableException(
                "The payment amount cannot be represented by PayOS.");
        }

        return decimal.ToInt64(amount);
    }

    private static long ToProviderExpiration(DateTime expiresAt)
    {
        var utc = expiresAt.Kind switch
        {
            DateTimeKind.Utc => expiresAt,
            DateTimeKind.Local => expiresAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)
        };
        var unixSeconds = new DateTimeOffset(utc).ToUnixTimeSeconds();
        if (unixSeconds <= 0 || unixSeconds > int.MaxValue)
        {
            throw new PaymentGatewayUnavailableException(
                "The payment expiration cannot be represented by PayOS.");
        }

        return unixSeconds;
    }

    private static string CreateDescription(long providerOrderCode)
    {
        if (providerOrderCode <= 0)
        {
            throw new PaymentGatewayUnavailableException(
                "The provider order code must be positive.");
        }

        return $"LM{providerOrderCode % 10_000_000:D7}";
    }

    private static PaymentGatewayOrderStatus MapStatus(PaymentLinkStatus status) => status switch
    {
        PaymentLinkStatus.Pending => PaymentGatewayOrderStatus.Pending,
        PaymentLinkStatus.Paid => PaymentGatewayOrderStatus.Paid,
        PaymentLinkStatus.Cancelled => PaymentGatewayOrderStatus.Cancelled,
        PaymentLinkStatus.Underpaid => PaymentGatewayOrderStatus.Underpaid,
        PaymentLinkStatus.Expired => PaymentGatewayOrderStatus.Expired,
        PaymentLinkStatus.Processing => PaymentGatewayOrderStatus.Processing,
        PaymentLinkStatus.Failed => PaymentGatewayOrderStatus.Failed,
        _ => PaymentGatewayOrderStatus.Unknown
    };

    private static bool IsProviderFailure(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception is PayOSException
            or HttpRequestException
            or TimeoutException
        || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
}
