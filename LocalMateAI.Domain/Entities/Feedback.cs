using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class Feedback : BaseEntity
{
    public Guid TripId { get; set; }
    public Guid UserId { get; set; }
    public FeedbackQuickTag QuickTag { get; set; }
    public string? Comment { get; set; }
}
