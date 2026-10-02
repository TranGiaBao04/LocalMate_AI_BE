namespace LocalMateAI.Domain.Entities;

public sealed class PaymentOrderCredit
{
    public Guid OrderId { get; init; }
    public Guid PeriodId { get; init; }
    public Guid UserId { get; init; }
    public DateTime OriginalEndsAt { get; init; }
    public int RemainingDays { get; init; }
    public decimal CalculatedCreditAmount { get; init; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? ReleaseProviderCheckedAt { get; set; }
    public string? ReleaseProviderStatus { get; set; }
    public decimal? ReleaseProviderRequestedAmount { get; set; }
    public decimal? ReleaseProviderAmountPaid { get; set; }
    public decimal? ReleaseProviderAmountRemaining { get; set; }
    public string? ReleaseReasonCode { get; set; }

    public void Release(DateTime atUtc, CreditReleaseEvidence evidence)
    {
        if (ReleasedAt is not null || !evidence.IsValid(atUtc))
            throw new InvalidOperationException("A credit claim can be released only once.");
        ReleasedAt = atUtc;
        ReleaseProviderCheckedAt = evidence.CheckedAt;
        ReleaseProviderStatus = evidence.Status;
        ReleaseProviderRequestedAmount = evidence.RequestedAmount;
        ReleaseProviderAmountPaid = evidence.AmountPaid;
        ReleaseProviderAmountRemaining = evidence.AmountRemaining;
        ReleaseReasonCode = evidence.ReasonCode;
    }

    public bool HasValidReleaseEvidence() => ReleasedAt is { } at
        && ReleaseProviderCheckedAt is { } checkedAt && ReleaseProviderStatus is { } status
        && ReleaseProviderRequestedAmount is { } amount && ReleaseProviderAmountPaid is { } paid
        && ReleaseProviderAmountRemaining is { } remaining && ReleaseReasonCode is { } reason
        && new CreditReleaseEvidence(checkedAt, status, amount, paid, remaining, reason).IsValid(at);

    public bool HasAnyReleaseEvidence() => ReleaseProviderCheckedAt is not null || ReleaseProviderStatus is not null
        || ReleaseProviderRequestedAmount is not null || ReleaseProviderAmountPaid is not null
        || ReleaseProviderAmountRemaining is not null || ReleaseReasonCode is not null;
}

public sealed record CreditReleaseEvidence(DateTime CheckedAt, string Status, decimal RequestedAmount,
    decimal AmountPaid, decimal AmountRemaining, string ReasonCode)
{
    public const string SafeReason = "provider_cancelled_no_funds";
    public bool IsValid(DateTime releasedAt) => CheckedAt.Kind == DateTimeKind.Utc && releasedAt.Kind == DateTimeKind.Utc
        && CheckedAt <= releasedAt && releasedAt - CheckedAt <= TimeSpan.FromMinutes(1)
        && Status == "Cancelled" && RequestedAmount > 0 && decimal.Truncate(RequestedAmount) == RequestedAmount
        && AmountPaid == 0 && AmountRemaining == RequestedAmount && ReasonCode == SafeReason;
}
