using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

public sealed class CuratedItinerary : BaseEntity
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public decimal EstimatedCostMin { get; set; }
    public decimal EstimatedCostMax { get; set; }
    public ICollection<CuratedItineraryItem> Items { get; set; } = [];
}
