namespace LocalMateAI.Application.Payments;

using LocalMateAI.Domain.Enums;

public sealed record PaymentLinkRequest(
    Guid OrderId,
    long ProviderOrderCode,
    string PlanCode,
    PaymentOrderType Type,
    decimal Amount,
    DateTime ExpiresAt);

public sealed record PaymentLinkResult(bool IsSuccess, string? CheckoutUrl, string? QrCode)
{
    public static PaymentLinkResult Succeeded(string checkoutUrl, string qrCode) =>
        new(true, checkoutUrl, qrCode);

    public static PaymentLinkResult Unavailable() => new(false, null, null);
}

public enum PaymentGatewayOrderStatus
{
    Pending,
    Paid,
    Cancelled,
    Underpaid,
    Expired,
    Processing,
    Failed,
    Unknown
}

public sealed record PaymentGatewayOrderResult(
    bool IsAvailable,
    long ProviderOrderCode,
    decimal Amount,
    PaymentGatewayOrderStatus Status)
{
    public static PaymentGatewayOrderResult Unavailable(long providerOrderCode) =>
        new(false, providerOrderCode, 0, PaymentGatewayOrderStatus.Unknown);
}

public sealed record VerifiedPaymentNotification(
    long ProviderOrderCode,
    decimal Amount,
    bool IsSuccessful);

public sealed record PaymentWebhookVerificationResult(
    bool IsValid,
    VerifiedPaymentNotification? Notification)
{
    public static PaymentWebhookVerificationResult Valid(
        VerifiedPaymentNotification notification) => new(true, notification);

    public static PaymentWebhookVerificationResult Invalid() => new(false, null);
}

public sealed class PaymentGatewayUnavailableException : Exception
{
    public PaymentGatewayUnavailableException(string message) : base(message)
    {
    }
}
