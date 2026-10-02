using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TripTotalsCalculatorTests
{
    [Fact]
    public void Calculate_NoStops_ReturnsZerosAndNoEndTime()
    {
        var totals = TripTotalsCalculator.Calculate([]);

        Assert.Equal(0, totals.TotalBudget);
        Assert.Equal(0, totals.TotalMinutes);
        Assert.Null(totals.EndTime);
    }

    [Fact]
    public void Calculate_SingleStop_HasNoTravelAndEndsAfterVisit()
    {
        var totals = TripTotalsCalculator.Calculate([Stop(9, 0, 60, 50000)]);

        Assert.Equal(60, totals.TotalVisitMinutes);
        Assert.Equal(0, totals.TotalTravelMinutes);
        Assert.Equal(60, totals.TotalMinutes);
        Assert.Equal(new TimeOnly(10, 0), totals.EndTime);
    }

    [Fact]
    public void Calculate_StopsWithGaps_SumsGapsAsTravel()
    {
        var totals = TripTotalsCalculator.Calculate([
            Stop(8, 0, 60, 30000),   // kết thúc 09:00
            Stop(9, 15, 75, 100000), // đi 15', kết thúc 10:30
            Stop(10, 40, 45, 0)      // đi 10', kết thúc 11:25
        ]);

        Assert.Equal(180, totals.TotalVisitMinutes);
        Assert.Equal(25, totals.TotalTravelMinutes);
        Assert.Equal(205, totals.TotalMinutes);
        Assert.Equal(new TimeOnly(11, 25), totals.EndTime);
    }

    [Fact]
    public void Calculate_OverlappingStops_ClampsNegativeGapToZero()
    {
        var totals = TripTotalsCalculator.Calculate([
            Stop(8, 0, 90, 0),
            Stop(9, 0, 60, 0) // bắt đầu trước khi chặng trước xong
        ]);

        Assert.Equal(0, totals.TotalTravelMinutes);
        Assert.Equal(150, totals.TotalMinutes);
    }

    [Fact]
    public void Calculate_SumsBudgetIncludingFreeStops()
    {
        var totals = TripTotalsCalculator.Calculate([
            Stop(8, 0, 60, 30000),
            Stop(9, 0, 60, 0),
            Stop(10, 0, 60, 120000)
        ]);

        Assert.Equal(150000, totals.TotalBudget);
    }

    private static TripStopSnapshot Stop(int hour, int minute, int durationMinutes, decimal budget) =>
        new(new TimeOnly(hour, minute), durationMinutes, budget);
}
