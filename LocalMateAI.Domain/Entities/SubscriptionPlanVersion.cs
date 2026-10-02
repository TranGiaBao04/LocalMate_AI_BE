using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;
namespace LocalMateAI.Domain.Entities;

public sealed class SubscriptionPlanVersion : BaseEntity
{
    public Guid PlanId { get; init; }
    public int VersionNumber { get; init; }
    public decimal Price { get; init; }
    public int? DurationDays { get; init; }
    public int? GenerateLimit { get; init; }
    public int? SavedTripLimit { get; init; }
    public PlanVersionOrigin Origin { get; init; }
    public DateTime? PublishedAt { get; init; }
}
