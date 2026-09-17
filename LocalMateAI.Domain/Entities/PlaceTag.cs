namespace LocalMateAI.Domain.Entities;

public sealed class PlaceTag
{
    public Guid PlaceId { get; set; }
    public Guid TagId { get; set; }
    public Place Place { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}