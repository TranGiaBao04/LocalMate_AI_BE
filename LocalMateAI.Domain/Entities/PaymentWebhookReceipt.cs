namespace LocalMateAI.Domain.Entities;

public sealed class PaymentWebhookReceipt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? PaymentOrderId { get; set; }
    public long ProviderOrderCode { get; init; }
    public decimal Amount { get; init; }
    public bool IsSuccessful { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string? RawPayload { get; set; }
    public string RawPayloadSha256 { get; init; } = string.Empty;
    public DateTime RawPayloadRetainUntil { get; init; }
    public DateTime? RawPayloadPurgedAt { get; set; }
}
