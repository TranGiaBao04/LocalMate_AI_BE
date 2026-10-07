using LocalMateAI.Application.DTOs.PublicReviews;
using LocalMateAI.Application.Validators.PublicReviews;

namespace LocalMateAI.Tests;

public sealed class PublicReviewQueryValidatorTests
{
    private static readonly PublicReviewQueryValidator Validator = new();

    [Fact]
    public void Limit_DefaultsToThree() =>
        Assert.Equal(3, new PublicReviewQuery().Limit);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void Limit_InRange_IsValid(int limit) =>
        Assert.True(Validator.Validate(new PublicReviewQuery { Limit = limit }).IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(13)]
    public void Limit_OutOfRange_FailsOnLimit(int limit)
    {
        var result = Validator.Validate(new PublicReviewQuery { Limit = limit });

        Assert.Equal("Limit", Assert.Single(result.Errors).PropertyName);
    }
}
