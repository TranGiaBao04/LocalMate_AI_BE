using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

public sealed class CuratedItineraryItem : BaseEntity
{
    public Guid CuratedItineraryId { get; set; }
    public CuratedItinerary CuratedItinerary { get; set; } = null!;
    public Guid PlaceId { get; set; }
    public Place Place { get; set; } = null!;
    public int OrderIndex { get; set; }
}
