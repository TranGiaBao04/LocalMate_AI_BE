using LocalMateAI.Domain.Common;
namespace LocalMateAI.Domain.Entities;

public sealed class SubscriptionPlan : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CurrentVersionId { get; set; }
    public int EntitlementPriority { get; set; }
}
