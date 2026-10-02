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

    public void Release(DateTime atUtc)
    {
        if (atUtc.Kind != DateTimeKind.Utc || ReleasedAt is not null)
            throw new InvalidOperationException("A credit claim can be released only once.");
        ReleasedAt = atUtc;
    }
}
