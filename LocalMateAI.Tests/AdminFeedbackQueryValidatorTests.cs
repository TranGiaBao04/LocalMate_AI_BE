using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.Validators.AdminFeedback;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminFeedbackQueryValidatorTests
{
    private static readonly AdminReviewQueryValidator Reviews = new();
    private static readonly AdminTripFeedbackQueryValidator Feedback = new();

    [Fact]
    public void EmptyQueries_AreValid()
    {
        Assert.True(Reviews.Validate(new AdminReviewQuery()).IsValid);
        Assert.True(Feedback.Validate(new AdminTripFeedbackQuery()).IsValid);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-06")]
    [InlineData("2026-10-06", "2026-10-06")]
    [InlineData("2026-10-01", null)]
    [InlineData(null, "2026-10-06")]
    public void DateRange_WellFormedAndOrdered_IsValid(string? from, string? to) =>
        Assert.True(Reviews.Validate(new AdminReviewQuery { From = from, To = to }).IsValid);

    [Theory]
    [InlineData("06/10/2026")]
    [InlineData("2026-13-01")]
    [InlineData("1999-12-31")]
    [InlineData("2101-01-01")]
    public void From_MalformedOrOutOfYearRange_FailsOnFrom(string from)
    {
        var result = Reviews.Validate(new AdminReviewQuery { From = from });

        Assert.Equal("From", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void To_BeforeFrom_FailsOnTo()
    {
        var result = Feedback.Validate(new AdminTripFeedbackQuery { From = "2026-10-06", To = "2026-10-05" });

        Assert.Equal("To", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ReviewRating_OutOfRange_FailsOnRating(int rating)
    {
        var result = Reviews.Validate(new AdminReviewQuery { Rating = rating });

        Assert.Equal("Rating", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void ReviewSortBy_AcceptsRating_ButFeedbackDoesNot()
    {
        Assert.True(Reviews.Validate(new AdminReviewQuery { SortBy = "rating" }).IsValid);

        var result = Feedback.Validate(new AdminTripFeedbackQuery { SortBy = "rating" });

        Assert.Equal("SortBy", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void FeedbackQuickTag_UndefinedValue_FailsOnQuickTag()
    {
        Assert.True(Feedback.Validate(new AdminTripFeedbackQuery { QuickTag = FeedbackQuickTag.TooFar }).IsValid);

        var result = Feedback.Validate(new AdminTripFeedbackQuery { QuickTag = (FeedbackQuickTag)99 });

        Assert.Equal("QuickTag", Assert.Single(result.Errors).PropertyName);
    }
}
