using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Validators.PlaceReviews;

namespace LocalMateAI.Tests;

public sealed class PlaceReviewQueryValidatorTests
{
    private static readonly PlaceReviewQueryValidator Validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(5)]
    public void Rating_MissingOrInRange_IsValid(int? rating) =>
        Assert.True(Validator.Validate(new PlaceReviewQuery { Rating = rating }).IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_OutOfRange_FailsOnRating(int rating)
    {
        var result = Validator.Validate(new PlaceReviewQuery { Rating = rating });

        Assert.Equal("Rating", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("createdAt")]
    [InlineData("RATING")]
    public void SortBy_MissingOrWhitelisted_IsValid(string? sortBy) =>
        Assert.True(Validator.Validate(new PlaceReviewQuery { SortBy = sortBy }).IsValid);

    [Fact]
    public void SortBy_Unknown_FailsOnSortBy()
    {
        var result = Validator.Validate(new PlaceReviewQuery { SortBy = "name" });

        Assert.Equal("SortBy", Assert.Single(result.Errors).PropertyName);
    }
}
