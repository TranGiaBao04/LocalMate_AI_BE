using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class Trip : BaseEntity
{
    public Guid? UserId { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public int DurationHours { get; set; }
    public decimal BudgetMin { get; set; }
    public decimal BudgetMax { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Draft;

    public ICollection<ItineraryItem> Items { get; set; } = [];
    public ICollection<TripTag> Tags { get; set; } = [];
}
