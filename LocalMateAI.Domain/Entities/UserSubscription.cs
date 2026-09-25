using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class UserSubscription : BaseEntity
{
    public Guid UserId { get; set; }
    public PlanCode PlanCode { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}
