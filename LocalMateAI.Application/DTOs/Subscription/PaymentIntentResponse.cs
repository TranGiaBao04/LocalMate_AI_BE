namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record PaymentIntentResponse(
    Guid OrderId,
    string QrCode,
    string CheckoutUrl,
    decimal Amount,
    DateTime ExpiresAt)
{
    public string Type { get; init; } = "Purchase";
    public decimal ListPrice { get; init; } = Amount;
    public decimal CreditAmount { get; init; }
}
