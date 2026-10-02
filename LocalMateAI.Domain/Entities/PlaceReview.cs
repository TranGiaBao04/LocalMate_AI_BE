using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

public sealed class PlaceReview : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid PlaceId { get; set; }
    public Guid ItineraryItemId { get; set; }
    public int Rating { get; set; }
    public string[] QuickTags { get; set; } = [];
    public string? Comment { get; set; }
}
