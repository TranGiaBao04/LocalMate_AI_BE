using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class SubscriptionCatalogTests
{
    [Fact]
    public void Catalog_ContainsExactCanonicalPlans()
    {
        Assert.Equal(3, SubscriptionCatalog.All.Count);
        Assert.Equal(new(PlanCode.Free, 0, null, 1, 1), SubscriptionCatalog.Get(PlanCode.Free));
        Assert.Equal(new(PlanCode.TripPass, 49_000, 7, null, 3), SubscriptionCatalog.Get(PlanCode.TripPass));
        Assert.Equal(new(PlanCode.Membership, 59_000, 30, null, null), SubscriptionCatalog.Get(PlanCode.Membership));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SubscriptionPlanDefinition>)SubscriptionCatalog.All)[0] =
                new SubscriptionPlanDefinition(PlanCode.Free, 1, null, null, null));
    }

    [Theory]
    [InlineData(-1, PlanCode.TripPass, null)]
    [InlineData(0, PlanCode.TripPass, null)]
    [InlineData(1, PlanCode.TripPass, PlanCode.TripPass)]
    public void EffectivePlan_UsesStrictExpiry(int secondsFromNow, PlanCode? candidate, PlanCode? expected)
    {
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        UserSubscription[] subscriptions = candidate is null
            ? []
            : new[] { new UserSubscription { PlanCode = candidate.Value, EndsAt = now.AddSeconds(secondsFromNow) } };

        Assert.Equal(expected, SubscriptionCatalog.ResolveEffectivePaid(subscriptions, now)?.PlanCode);
    }

    [Fact]
    public void EffectivePlan_ExpiredTripPassFallsBackToFree()
    {
        var now = DateTime.UtcNow;
        var subscriptions = new[] { new UserSubscription { PlanCode = PlanCode.TripPass, EndsAt = now.AddTicks(-1) } };

        Assert.Null(SubscriptionCatalog.ResolveEffectivePaid(subscriptions, now));
    }

    [Fact]
    public void EffectivePlan_MembershipWinsOverTripPass()
    {
        var now = DateTime.UtcNow;
        var subscriptions = new[]
        {
            new UserSubscription { PlanCode = PlanCode.TripPass, EndsAt = now.AddDays(7) },
            new UserSubscription { PlanCode = PlanCode.Membership, EndsAt = now.AddHours(1) }
        };

        Assert.Equal(PlanCode.Membership, SubscriptionCatalog.ResolveEffectivePaid(subscriptions, now)?.PlanCode);
    }

    [Theory]
    [InlineData("2026-09-30T16:59:59Z", "2026-08-31T17:00:00Z", "2026-09-30T17:00:00Z")]
    [InlineData("2026-09-30T17:00:00Z", "2026-09-30T17:00:00Z", "2026-10-31T17:00:00Z")]
    public void VietnamMonth_UsesLocalCalendarBoundaries(string now, string start, string next)
    {
        var window = VietnamMonthWindow.For(DateTime.Parse(now).ToUniversalTime());

        Assert.Equal(DateTime.Parse(start).ToUniversalTime(), window.StartUtc);
        Assert.Equal(DateTime.Parse(next).ToUniversalTime(), window.NextStartUtc);
    }
}
