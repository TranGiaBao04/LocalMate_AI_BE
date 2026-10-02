using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

public sealed class PlanFeature : BaseEntity
{
    public string Code { get; init; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystem { get; init; }
}
