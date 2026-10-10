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
    // Xoá mềm: khác null thì lịch mẫu biến khỏi app và Admin. Mọi truy vấn phải lọc tường minh.
    public DateTime? DeletedAt { get; set; }
    public ICollection<CuratedItineraryItem> Items { get; set; } = [];
}
