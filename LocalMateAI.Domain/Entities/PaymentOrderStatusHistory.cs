using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class PaymentOrderStatusHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PaymentOrderId { get; init; }
    public PaymentOrderStatus? FromStatus { get; init; }
    public PaymentOrderStatus ToStatus { get; init; }
    public PaymentStatusChangeSource Source { get; init; }
    public string? ReasonCode { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid? WebhookReceiptId { get; init; }
}
