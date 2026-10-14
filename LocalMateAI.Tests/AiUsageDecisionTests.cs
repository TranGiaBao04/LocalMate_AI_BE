using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Tests;

public sealed class AiUsageDecisionTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(30, 30)]
    [InlineData(12.4, 13)]
    [InlineData(0.2, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void RetryAfterSeconds_RoundsUp_AndIsAtLeastOne(double secondsLeft, int expected)
    {
        var decision = new AiUsageDecision(AiUsageStatus.CoolingDown, RetryAtUtc: Now.AddSeconds(secondsLeft));

        Assert.Equal(expected, decision.RetryAfterSeconds(Now));
    }

    [Fact]
    public void RetryAfterSeconds_IsZero_WhenThereIsNoCooldown()
    {
        Assert.Equal(0, new AiUsageDecision(AiUsageStatus.Allowed).RetryAfterSeconds(Now));
    }
}
