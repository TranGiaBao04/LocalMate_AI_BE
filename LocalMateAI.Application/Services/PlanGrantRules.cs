using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Services;

namespace LocalMateAI.Application.Services;

// Luật của gói tặng (logic thuần, không chạm DB).
public static class PlanGrantRules
{
    public const int MinDays = 1;
    public const int MaxDays = 90;
    public const int MaxReasonLength = 500;

    // Gói tặng không được kết thúc xa hơn số ngày này kể từ lúc tặng.
    public const int MaxHorizonDays = 365;

    // Gói tặng bắt đầu ngay, hoặc ngay sau phần đã được phủ bởi gói có độ ưu tiên bằng hoặc cao hơn
    // (kỳ đã mua còn hiệu lực hoặc chưa bắt đầu, gói tặng chưa hết hạn) để không ngày tặng nào bị che mất.
    public static DateTime StartsAt(
        DateTime nowUtc,
        int targetPriority,
        IReadOnlyCollection<SubscriptionPeriod> periods,
        IReadOnlyCollection<PlanGrant> grants,
        IReadOnlyDictionary<Guid, int> priorityByPlanId) =>
        periods.Where(SubscriptionPeriodLifecycle.HasEffectiveDuration)
            .Select(period => (period.PlanId, End: SubscriptionPeriodLifecycle.EffectiveEnd(period)))
            .Concat(grants.Select(grant => (grant.PlanId, End: grant.EndsAt)))
            .Where(covered => covered.End > nowUtc
                && priorityByPlanId.TryGetValue(covered.PlanId, out var priority)
                && priority >= targetPriority)
            .Select(covered => covered.End)
            .Append(nowUtc)
            .Max();

    // Hết chuỗi gói tặng liền nhau: gói kế tiếp phải bắt đầu đúng lúc gói trước kết thúc.
    public static DateTime ChainEnd(
        DateTime endsAt,
        IEnumerable<(DateTime StartsAt, DateTime EndsAt)> otherGrantsOfSamePlan)
    {
        foreach (var next in otherGrantsOfSamePlan.Where(grant => grant.StartsAt >= endsAt)
                     .OrderBy(grant => grant.StartsAt).ToArray())
        {
            if (next.StartsAt != endsAt)
            {
                break;
            }

            endsAt = next.EndsAt;
        }

        return endsAt;
    }
}
