namespace LocalMateAI.Application.DTOs.Payments;

public sealed record AdminTransactionDetailResponse(
    AdminTransactionDetailTransactionResponse Transaction,
    IReadOnlyList<AdminTransactionStatusHistoryResponse> StatusHistory,
    IReadOnlyList<AdminTransactionWebhookReceiptResponse> WebhookReceipts);

public sealed record AdminTransactionDetailTransactionResponse : AdminTransactionResponse
{
    public AdminTransactionDetailTransactionResponse(AdminTransactionResponse transaction,
        Guid? planId, Guid? planVersionId, string planVersionBinding, DateTime? updatedAt) : base(transaction)
    {
        PlanId = planId;
        PlanVersionId = planVersionId;
        PlanVersionBinding = planVersionBinding;
        UpdatedAt = updatedAt;
    }

    public Guid? PlanId { get; init; }
    public Guid? PlanVersionId { get; init; }
    public string PlanVersionBinding { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record AdminTransactionStatusHistoryResponse(
    Guid Id, string? FromStatus, string ToStatus, string Source, string? ReasonCode,
    DateTime OccurredAt, Guid? ActorUserId, Guid? WebhookReceiptId);

public sealed record AdminTransactionWebhookReceiptResponse(
    Guid Id, long ProviderOrderCode, decimal Amount, bool IsSuccessful, DateTime ReceivedAt,
    string RawPayloadSha256, bool HasRawPayload, DateTime RawPayloadRetainUntil, DateTime? RawPayloadPurgedAt);
