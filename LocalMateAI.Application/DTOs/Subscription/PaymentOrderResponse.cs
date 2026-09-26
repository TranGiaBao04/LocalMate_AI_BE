namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record PaymentOrderResponse(
    Guid OrderId,
    string Status,
    string PlanCode,
    decimal Amount,
    DateTime ExpiresAt,
    DateTime? PaidAt);
