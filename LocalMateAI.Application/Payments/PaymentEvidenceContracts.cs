using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Payments;

public sealed record PaymentTransitionContext(PaymentStatusChangeSource Source,
    Guid? ActorUserId = null, Guid? WebhookReceiptId = null, string? ReasonCode = null)
{
    public PaymentOrderStatusHistory History(PaymentOrder order, PaymentOrderStatus? from, DateTime now,
        string? reason = null) => new()
    {
        PaymentOrderId = order.Id, FromStatus = from, ToStatus = order.Status, Source = Source,
        ActorUserId = ActorUserId, WebhookReceiptId = WebhookReceiptId,
        ReasonCode = reason ?? ReasonCode, OccurredAt = now
    };
}

public sealed class PaymentEvidenceOptions
{
    public const string SectionName = "PaymentEvidence";
    public const int MaximumRawPayloadBytes = 64 * 1024;
    public int RawWebhookRetentionDays { get; set; } = 30;
}
