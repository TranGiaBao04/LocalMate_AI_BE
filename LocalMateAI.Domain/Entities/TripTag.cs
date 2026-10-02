namespace LocalMateAI.Domain.Entities;

public sealed class TripTag
{
    public Guid TripId { get; set; }

    public Guid TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
