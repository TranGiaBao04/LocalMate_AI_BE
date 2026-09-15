using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

public sealed class ItineraryItem : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;
    public Guid PlaceId { get; set; }
    public Place Place { get; set; } = null!;
    public int OrderIndex { get; set; }
    public TimeOnly ScheduledTime { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public decimal EstimatedBudget { get; set; }
    public string? Reasoning { get; set; }
}
