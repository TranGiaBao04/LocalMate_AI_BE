using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Services;

namespace LocalMateAI.Application.Services;

public static class UpgradeCreditCalculator
{
    public const decimal MinimumUpgradeAmount = 10_000m;

    public static UpgradeCreditResult Calculate(UpgradeCreditInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Sources);
        ValidateMoney(input.TargetPrice, true);
        var today = VietnamDate(input.NowUtc);
        var seen = new HashSet<Guid>();
        var rows = new List<UpgradeCreditSourceResult>();
        var rawTotal = 0m;
        foreach (var source in input.Sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.PeriodId == Guid.Empty || !seen.Add(source.PeriodId))
                throw new ArgumentException("Credit sources must have distinct nonempty identities.");
            ValidateMoney(source.PurchasedPrice, false);
            if (source.PurchasedDurationDays <= 0)
                throw new ArgumentOutOfRangeException(nameof(source.PurchasedDurationDays));
            var startDate = VietnamDate(source.StartsAt);
            RequireUtc(source.EndsAt);
            if (source.TerminatedAt is { } terminatedAt) RequireUtc(terminatedAt);
            if (source.StartsAt >= source.EndsAt)
                throw new ArgumentException("A source must have a valid original interval.");
            var end = SubscriptionPeriodLifecycle.EffectiveEnd(source.EndsAt, source.TerminatedAt);
            if (end <= source.StartsAt || input.NowUtc >= end) continue;

            var usedDays = source.StartsAt > input.NowUtc ? 0
                : checked(today.DayNumber - startDate.DayNumber + 1);
            var remaining = Math.Clamp(checked(source.PurchasedDurationDays - usedDays), 0,
                source.PurchasedDurationDays);
            // Multiply before dividing: division-first can lose a VND when flooring a full future term.
            var credit = source.MonetaryCreditEligible
                ? decimal.Floor(checked(source.PurchasedPrice * remaining) / source.PurchasedDurationDays) : 0m;
            rows.Add(new(source.PeriodId, remaining, credit));
            rawTotal = checked(rawTotal + credit);
        }
        var rounded = checked(decimal.Floor(rawTotal / 1000m) * 1000m);
        var minimum = Math.Min(input.TargetPrice, MinimumUpgradeAmount);
        var payable = Math.Max(checked(input.TargetPrice - rounded), minimum);
        return new(input.TargetPrice, rawTotal, rounded, checked(input.TargetPrice - payable), payable,
            rows.AsReadOnly());
    }

    private static DateOnly VietnamDate(DateTime utc)
    {
        RequireUtc(utc);
        return DateOnly.FromDateTime(utc + VietnamTime.Offset);
    }

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Credit timestamps must be UTC.");
    }

    private static void ValidateMoney(decimal price, bool positive)
    {
        if (price < 0 || (positive && price == 0) || price != decimal.Truncate(price))
            throw new ArgumentOutOfRangeException(nameof(price), "Prices must be whole VND.");
    }
}
