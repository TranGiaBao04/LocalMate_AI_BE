using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Services;

namespace LocalMateAI.Tests;

public sealed class UpgradeCreditCalculatorTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static UpgradeCreditSource Source(decimal price = 19000, int days = 7, bool eligible = true) =>
        new(Guid.NewGuid(), Start, Start.AddDays(days), null, price, days, eligible);
    private static UpgradeCreditResult Calculate(DateTime now, params UpgradeCreditSource[] sources) =>
        UpgradeCreditCalculator.Calculate(new(59000, now, sources));

    [Theory]
    [InlineData(0, 6, 16285, 16000)]
    [InlineData(2, 4, 10857, 10000)]
    [InlineData(6, 0, 0, 0)]
    [InlineData(-1, 7, 19000, 19000)]
    public void VietnamTouchedDays(int elapsed, int remaining, int credit, int rounded)
    {
        var result = Calculate(Start.AddDays(elapsed), Source());
        var row = Assert.Single(result.Sources);
        Assert.Equal(remaining, row.RemainingDays);
        Assert.Equal(credit, row.CalculatedCreditAmount);
        Assert.Equal(rounded, result.RoundedCreditTotal);
        Assert.Equal(result.ListPrice, result.AmountPayable + result.AppliedCredit);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void EndedPeriodsExcluded(int days) => Assert.Empty(Calculate(Start.AddDays(days), Source()).Sources);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void EmptyTerminatedPeriodExcluded(int days) =>
        Assert.Empty(Calculate(Start, Source() with { TerminatedAt = Start.AddDays(days) }).Sources);

    [Fact]
    public void AggregateFloorsOnce_NotEachSourceToThousands()
    {
        var result = Calculate(Start, Source(1000, 3), Source(1000, 3));
        Assert.All(result.Sources, row => Assert.Equal(666, row.CalculatedCreditAmount));
        Assert.Equal(1332, result.RawCreditTotal);
        Assert.Equal(1000, result.RoundedCreditTotal);
    }

    [Theory]
    [InlineData(59000, 10000, 49000)]
    [InlineData(10000, 10000, 0)]
    [InlineData(9000, 9000, 0)]
    [InlineData(1, 1, 0)]
    public void MinimumAndSurplusNoCarry(int target, int payable, int applied)
    {
        var result = UpgradeCreditCalculator.Calculate(new(target, Start.AddDays(-1), [Source(100000, 30)]));
        Assert.Equal(payable, result.AmountPayable);
        Assert.Equal(applied, result.AppliedCredit);
        Assert.Equal(100000, Assert.Single(result.Sources).CalculatedCreditAmount);
    }

    [Theory]
    [InlineData(19000)]
    [InlineData(0)]
    public void UnprovenOrFreeManualKeepsZeroReplacementRow(int price)
    {
        var result = Calculate(Start, Source(price, eligible: false));
        Assert.Equal(0, Assert.Single(result.Sources).CalculatedCreditAmount);
        Assert.Equal(6, result.Sources[0].RemainingDays);
        Assert.Equal(59000, result.AmountPayable);
    }

    [Fact]
    public void UsesPurchasedPrice_NotCurrentCatalog_AndCheaperTargetIsValid()
    {
        var result = UpgradeCreditCalculator.Calculate(new(15000, Start, [Source(19000)]));
        Assert.Equal(16285, Assert.Single(result.Sources).CalculatedCreditAmount);
        Assert.Equal(5000, result.AppliedCredit);
        Assert.Equal(10000, result.AmountPayable);
    }

    [Fact]
    public void VietnamMidnightNotMachineLocal()
    {
        var before = new DateTime(2026, 10, 1, 16, 59, 59, DateTimeKind.Utc);
        Assert.Equal(6, Assert.Single(Calculate(before, Source()).Sources).RemainingDays);
        Assert.Equal(5, Assert.Single(Calculate(before.AddSeconds(1), Source()).Sources).RemainingDays);
    }

    [Fact]
    public void MultiplyBeforeDivide_PreservesFullFuturePrice()
    {
        Assert.Equal(19000, Assert.Single(Calculate(Start.AddDays(-1), Source()).Sources).CalculatedCreditAmount);
        Assert.Equal(100, Assert.Single(Calculate(Start.AddDays(-1), Source(100, 3)).Sources).CalculatedCreditAmount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidDurationRejected(int days) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        Calculate(Start, Source() with { PurchasedDurationDays = days }));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1.5)]
    public void InvalidTargetRejected(double price) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        UpgradeCreditCalculator.Calculate(new((decimal)price, Start, [])));

    [Theory]
    [InlineData(-1)]
    [InlineData(1.5)]
    public void InvalidPurchasedPriceRejected(double price) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        Calculate(Start, Source() with { PurchasedPrice = (decimal)price }));

    [Theory]
    [InlineData("now")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("termination")]
    public void NonUtcRejected(string field)
    {
        var source = Source();
        var invalid = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified);
        var now = field == "now" ? invalid : Start;
        source = field switch
        {
            "start" => source with { StartsAt = invalid },
            "end" => source with { EndsAt = invalid },
            "termination" => source with { TerminatedAt = invalid },
            _ => source
        };
        Assert.Throws<ArgumentException>(() => Calculate(now, source));
    }

    [Fact]
    public void CheckedMoneyAndVietnamDateOverflow()
    {
        Assert.Throws<OverflowException>(() => Calculate(Start.AddDays(-1), Source(decimal.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Calculate(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), Source()));
    }

    [Fact]
    public void InvalidIdentityDuplicateAndIntervalRejected()
    {
        var source = Source();
        Assert.Throws<ArgumentException>(() => Calculate(Start, source, source));
        Assert.Throws<ArgumentException>(() => Calculate(Start, source with { PeriodId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => Calculate(Start, source with { EndsAt = Start }));
    }

    [Fact]
    public void EffectiveEndAndHalfOpenEmptyFuture()
    {
        var p = new SubscriptionPeriod { StartsAt = Start, EndsAt = Start.AddDays(7) };
        Assert.Equal(p.EndsAt, SubscriptionPeriodLifecycle.EffectiveEnd(p));
        Assert.True(SubscriptionPeriodLifecycle.IsEffectiveAt(p, Start));
        Assert.False(SubscriptionPeriodLifecycle.IsEffectiveAt(p, p.EndsAt));
        p.Terminate(Start.AddDays(-1), Guid.NewGuid());
        Assert.False(SubscriptionPeriodLifecycle.HasEffectiveDuration(p));
        Assert.False(SubscriptionPeriodLifecycle.IsEffectiveAt(p, Start));
        Assert.Equal(Start.AddDays(7), p.EndsAt);
        Assert.Throws<InvalidOperationException>(() => p.Terminate(Start, Guid.NewGuid()));
    }

    [Fact]
    public void ReleaseIsOneWayUtc()
    {
        var row = new PaymentOrderCredit();
        var proof = new CreditReleaseEvidence(Start, "Cancelled", 10000, 0, 10000, CreditReleaseEvidence.SafeReason);
        Assert.Throws<InvalidOperationException>(() => row.Release(DateTime.SpecifyKind(Start, DateTimeKind.Unspecified), proof));
        row.Release(Start, proof);
        Assert.Throws<InvalidOperationException>(() => row.Release(Start.AddDays(1), proof));
    }
}
