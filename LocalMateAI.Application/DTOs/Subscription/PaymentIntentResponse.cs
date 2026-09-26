namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record PaymentIntentResponse(
    Guid OrderId,
    string QrCode,
    string CheckoutUrl,
    decimal Amount,
    DateTime ExpiresAt);
