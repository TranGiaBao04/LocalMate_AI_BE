using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Validators.Places;
using LocalMateAI.Domain.Enums;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class AdminPlaceQueryValidatorTests
{
    private readonly AdminPlaceQueryValidator _validator = new();

    [Fact]
    public void DefaultQuery_IsValid()
    {
        var result = _validator.Validate(new AdminPlaceQuery());
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("address")]
    [InlineData("category")]
    [InlineData("status")]
    [InlineData("isVerified")]
    [InlineData("estimatedCostMin")]
    [InlineData("estimatedCostMax")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    public void AllowedSortFields_AreValid(string sortBy)
    {
        var query = new AdminPlaceQuery { SortBy = sortBy };
        var result = _validator.Validate(query);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidSortField_FailsValidation()
    {
        var query = new AdminPlaceQuery { SortBy = "unsupported_column" };
        var result = _validator.Validate(query);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdminPlaceQuery.SortBy));
    }

    [Fact]
    public void InvalidCategoryEnum_FailsValidation()
    {
        var query = new AdminPlaceQuery { Category = (PlaceCategory)999 };
        var result = _validator.Validate(query);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdminPlaceQuery.Category));
    }

    [Fact]
    public void InvalidStatusEnum_FailsValidation()
    {
        var query = new AdminPlaceQuery { Status = (PlaceStatus)999 };
        var result = _validator.Validate(query);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdminPlaceQuery.Status));
    }
}
