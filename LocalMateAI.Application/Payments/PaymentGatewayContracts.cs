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

public sealed class PaymentGatewayUnavailableException : Exception
{
    public PaymentGatewayUnavailableException(string message) : base(message)
    {
    }
}
