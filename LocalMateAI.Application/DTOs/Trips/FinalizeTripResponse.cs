namespace LocalMateAI.Application.DTOs.Trips;

public sealed record FinalizeTripResponse(Guid TripId, string Status)
{
    public string FundingSource { get; init; } = "Normal";
    public Guid? EntitlementId { get; init; }
    public DateTime? ConsumedAt { get; init; }
}
