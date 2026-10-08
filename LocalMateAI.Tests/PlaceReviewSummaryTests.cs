using System.Globalization;
using LocalMateAI.Application.DTOs.PlaceReviews;

namespace LocalMateAI.Tests;

public sealed class PlaceReviewSummaryTests
{
    [Fact]
    public void NoReviews_HasNoAverage() =>
        Assert.Null(new PlaceReviewSummary(0, 0).AverageRating);

    [Theory]
    [InlineData(1, 5, "5")]
    [InlineData(3, 13, "4.3")]  // 4,333…
    [InlineData(4, 17, "4.3")]  // 4,25 làm tròn lên
    [InlineData(20, 87, "4.4")] // 4,35 làm tròn lên (double sẽ ra 4,3)
    public void Average_IsRoundedHalfUp_ToOneDecimal(int reviewCount, int ratingSum, string expected) =>
        Assert.Equal(
            decimal.Parse(expected, CultureInfo.InvariantCulture),
            new PlaceReviewSummary(reviewCount, ratingSum).AverageRating);
}
