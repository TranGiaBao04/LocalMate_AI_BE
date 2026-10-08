using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
namespace LocalMateAI.Infrastructure.Persistence;

// Import baseline, not a runtime catalog. Historical publication dates are deliberately unknown.
public static class SubscriptionBaseline
{
    public static Guid PlanId(PlanCode code) => new($"10000000-0000-0000-0000-{(int)code + 1:000000000000}");
    public static Guid VersionId(PlanCode code) => new($"20000000-0000-0000-0000-{(int)code + 1:000000000000}");
    public static SubscriptionPlan[] Plans() =>
    [
        Plan(PlanCode.Free, "FREE", "Free", 0),
        Plan(PlanCode.TripPass, "TRIP_PASS", "Trip Pass", 100),
        Plan(PlanCode.Membership, "MEMBERSHIP", "Membership", 200)
    ];
    private static SubscriptionPlan Plan(PlanCode code, string canonical, string name, int priority) =>
        new()
        {
            Id = PlanId(code),
            Code = canonical,
            Name = name,
            IsSystem = true,
            IsActive = true,
            EntitlementPriority = priority,
            CurrentVersionId = VersionId(code)
        };
    public static SubscriptionPlanVersion[] Versions() =>
    [
        Version(PlanCode.Free, 0, null, 1, 1, 3, 1),
        Version(PlanCode.TripPass, 19000, 7, null, 3, 15, 3),
        Version(PlanCode.Membership, 59000, 30, null, null, 30, 3)
    ];
    private static SubscriptionPlanVersion Version(PlanCode code, decimal price, int? duration, int? generate, int? saved,
        int aiDaily, int aiExplain) =>
        new()
        {
            Id = VersionId(code),
            PlanId = PlanId(code),
            VersionNumber = 1,
            Price = price,
            DurationDays = duration,
            GenerateLimit = generate,
            SavedTripLimit = saved,
            AiDailyCallLimit = aiDaily,
            AiExplainCallsPerTripLimit = aiExplain,
            Origin = PlanVersionOrigin.LegacyBaseline
        };
}
