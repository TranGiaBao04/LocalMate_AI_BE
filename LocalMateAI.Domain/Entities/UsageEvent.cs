using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class UsageEvent : BaseEntity
{
    public Guid UserId { get; set; }
    public UsageEventType Type { get; set; }
    public Guid TripId { get; set; }
}
