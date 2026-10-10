using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Validators.Itineraries;

namespace LocalMateAI.Tests;

public sealed class AdminCuratedItineraryQueryValidatorTests
{
    private readonly AdminCuratedItineraryQueryValidator validator = new();

    [Fact]
    public void DefaultQuery_IsValid()
    {
        Assert.True(validator.Validate(new AdminCuratedItineraryQuery()).IsValid);
    }

    [Theory]
    [InlineData("updatedAt")]
    [InlineData("CREATEDAT")]
    [InlineData("title")]
    public void AllowedSortFields_AreValid(string sortBy)
    {
        Assert.True(validator.Validate(new AdminCuratedItineraryQuery { SortBy = sortBy }).IsValid);
    }

    [Fact]
    public void UnknownSortField_FailsOnSortBy()
    {
        var result = validator.Validate(new AdminCuratedItineraryQuery { SortBy = "estimatedCostMax" });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AdminCuratedItineraryQuery.SortBy));
    }

    [Fact]
    public void PageSizeAboveLimit_FailsOnPageSize()
    {
        var result = validator.Validate(new AdminCuratedItineraryQuery { PageSize = 101 });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(AdminCuratedItineraryQuery.PageSize));
    }
}
