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
    public DateTime? FinalizedAt { get; set; }

    // Xoá mềm: null = còn hoạt động. Giữ dòng lại vì Feedback/PlaceReview trỏ tới Trip bằng FK Restrict.
    public DateTime? DeletedAt { get; set; }

    public ICollection<ItineraryItem> Items { get; set; } = [];
    public ICollection<TripTag> Tags { get; set; } = [];
}
