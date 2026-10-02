namespace LocalMateAI.Domain.Entities;

public sealed class SingleItineraryEntitlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SourcePaymentOrderId { get; set; }
    public Guid SingleItineraryProductVersionId { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public Guid? ConsumedTripId { get; set; }

    public void Consume(Guid tripId, DateTime nowUtc)
    {
        if (ConsumedAt is not null || ConsumedTripId is not null || tripId == Guid.Empty
            || nowUtc.Kind != DateTimeKind.Utc || nowUtc < GrantedAt)
            throw new InvalidOperationException("Entitlement cannot be consumed.");
        ConsumedTripId = tripId;
        ConsumedAt = nowUtc;
    }
}
