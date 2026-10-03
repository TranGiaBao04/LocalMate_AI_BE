using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

public sealed class PlaceImage : BaseEntity
{
    public Guid PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public required string Url { get; set; }
    public string? Caption { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
}
