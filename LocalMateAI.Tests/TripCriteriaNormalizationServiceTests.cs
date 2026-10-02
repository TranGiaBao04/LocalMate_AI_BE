using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TripCriteriaNormalizationServiceTests
{
    private readonly TripCriteriaNormalizationService _sut = new();

    [Theory]
    [InlineData(1, TripDurationCategory.Short)]
    [InlineData(3, TripDurationCategory.Short)]
    [InlineData(4, TripDurationCategory.HalfDay)]
    [InlineData(6, TripDurationCategory.HalfDay)]
    [InlineData(7, TripDurationCategory.FullDay)]
    public void Normalize_ClassifiesDurationCorrectly(int hours, TripDurationCategory expected)
    {
        var criteria = _sut.Normalize(Request(durationHours: hours, budgetMax: 300_000m));

        Assert.Equal(expected, criteria.DurationCategory);
    }

    [Theory]
    [InlineData(100_000, BudgetTier.Economy)]
    [InlineData(200_000, BudgetTier.Economy)]
    [InlineData(300_000, BudgetTier.Standard)]
    [InlineData(500_000, BudgetTier.Standard)]
    [InlineData(600_000, BudgetTier.Premium)]
    public void Normalize_ClassifiesBudgetTier(decimal budgetMax, BudgetTier expected)
    {
        var criteria = _sut.Normalize(Request(durationHours: 4, budgetMax: budgetMax));

        Assert.Equal(expected, criteria.BudgetTier);
    }

    [Fact]
    public void Normalize_KeepsBudgetMaxForWholeTrip()
    {
        // Số chặng do ItineraryScheduler quyết định; normalization chỉ giữ tổng ngân sách của cả chuyến.
        var criteria = _sut.Normalize(Request(durationHours: 6, budgetMax: 600_000m));

        Assert.Equal(600_000m, criteria.BudgetMax);
    }

    private static TripRequestDto Request(int durationHours, decimal budgetMax) =>
        new(
            StartLatitude: 10.77,
            StartLongitude: 106.69,
            DurationHours: durationHours,
            BudgetMin: 0,
            BudgetMax: budgetMax,
            TagIds: []);
}