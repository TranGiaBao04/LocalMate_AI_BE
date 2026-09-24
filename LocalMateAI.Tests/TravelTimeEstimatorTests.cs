using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TravelTimeEstimatorTests
{
    [Fact]
    public void RoadDistanceKm_SamePoint_IsZero()
    {
        Assert.Equal(0, TravelTimeEstimator.RoadDistanceKm(10.77, 106.70, 10.77, 106.70));
    }

    [Fact]
    public void RoadDistanceKm_AppliesDetourFactorToStraightLine()
    {
        // 0,01° vĩ độ ≈ 1,112 km đường chim bay → × 1,3 ≈ 1,45 km
        Assert.Equal(1.45, TravelTimeEstimator.RoadDistanceKm(10.77, 106.70, 10.78, 106.70), 2);
    }

    [Fact]
    public void Minutes_NeverBelowOne()
    {
        Assert.Equal(1, TravelTimeEstimator.WalkingMinutes(0));
        Assert.Equal(1, TravelTimeEstimator.MotorbikeMinutes(0));
    }

    [Fact]
    public void EstimateMinutes_WalkingAndMotorbike_UseTheirOwnSpeed()
    {
        Assert.Equal(38, TravelTimeEstimator.EstimateMinutes(3.0, TravelMode.Walking));
        Assert.Equal(8, TravelTimeEstimator.EstimateMinutes(3.0, TravelMode.Motorbike));
    }

    [Fact]
    public void EstimateMinutes_Auto_WalksUpToThresholdThenUsesMotorbike()
    {
        Assert.Equal(9, TravelTimeEstimator.EstimateMinutes(0.70, TravelMode.Auto)); // đúng ngưỡng: đi bộ
        Assert.Equal(2, TravelTimeEstimator.EstimateMinutes(0.71, TravelMode.Auto)); // vượt ngưỡng: xe máy
    }
}
