namespace LocalMateAI.Domain.Entities;

public sealed class SingleItineraryProductVersion
{
    public const string ProductCode = "SINGLE_ITINERARY";
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime PublishedAt { get; set; }
}
