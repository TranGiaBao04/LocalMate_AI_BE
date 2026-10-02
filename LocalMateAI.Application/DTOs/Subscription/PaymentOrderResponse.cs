namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record PaymentOrderResponse(
    Guid OrderId,
    string Status,
    string PlanCode,
    decimal Amount,
    DateTime ExpiresAt,
    DateTime? PaidAt)
{
    public string Type { get; init; } = "Purchase";
    public decimal ListPrice { get; init; } = Amount;
    public decimal CreditAmount { get; init; }
}
