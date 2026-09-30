using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Tests;

public sealed class PagedQueryValidatorTests
{
    private static readonly string[] SortFields = ["createdAt", "name"];
    private readonly TestQueryValidator validator = new();

    [Fact]
    public void Validate_Defaults_IsValid()
    {
        Assert.True(validator.Validate(new PagedQuery()).IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PagedQuery.MaxPage)]
    public void Validate_PageAtBoundary_IsValid(int page)
    {
        Assert.True(validator.Validate(new PagedQuery { Page = page }).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PagedQuery.MaxPage + 1)]
    public void Validate_PageOutOfRange_FailsOnPage(int page)
    {
        AssertFailsOn(new PagedQuery { Page = page }, nameof(PagedQuery.Page));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PagedQuery.MaxPageSize)]
    public void Validate_PageSizeAtBoundary_IsValid(int pageSize)
    {
        Assert.True(validator.Validate(new PagedQuery { PageSize = pageSize }).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(PagedQuery.MaxPageSize + 1)]
    public void Validate_PageSizeOutOfRange_FailsOnPageSize(int pageSize)
    {
        AssertFailsOn(new PagedQuery { PageSize = pageSize }, nameof(PagedQuery.PageSize));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("createdAt")]
    [InlineData("CREATEDAT")]
    [InlineData(" name ")]
    public void Validate_AllowedOrEmptySortBy_IsValid(string? sortBy)
    {
        Assert.True(validator.Validate(new PagedQuery { SortBy = sortBy }).IsValid);
    }

    [Fact]
    public void Validate_UnknownSortBy_FailsAndListsAllowedFields()
    {
        var result = validator.Validate(new PagedQuery { SortBy = "passwordHash" });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(PagedQuery.SortBy), error.PropertyName);
        Assert.Contains("createdAt, name", error.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("asc")]
    [InlineData("DESC")]
    [InlineData(" desc ")]
    public void Validate_ValidSortDirection_IsValid(string? sortDirection)
    {
        Assert.True(validator.Validate(new PagedQuery { SortDirection = sortDirection }).IsValid);
    }

    [Theory]
    [InlineData("up")]
    [InlineData("ascending")]
    public void Validate_InvalidSortDirection_FailsOnSortDirection(string sortDirection)
    {
        AssertFailsOn(new PagedQuery { SortDirection = sortDirection }, nameof(PagedQuery.SortDirection));
    }

    [Fact]
    public void Validate_SearchAtMaxLengthAfterTrim_IsValid()
    {
        var search = "  " + new string('a', PagedQuery.MaxSearchLength) + "  ";

        Assert.True(validator.Validate(new PagedQuery { Search = search }).IsValid);
    }

    [Fact]
    public void Validate_SearchTooLong_FailsOnSearch()
    {
        var search = new string('a', PagedQuery.MaxSearchLength + 1);

        AssertFailsOn(new PagedQuery { Search = search }, nameof(PagedQuery.Search));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  cafe  ", "cafe")]
    public void NormalizedSearch_TrimsAndTreatsBlankAsNull(string? search, string? expected)
    {
        Assert.Equal(expected, new PagedQuery { Search = search }.NormalizedSearch);
    }

    [Fact]
    public void Constructor_EmptySortFields_Throws()
    {
        Assert.Throws<ArgumentException>(() => new EmptySortFieldsValidator());
    }

    private void AssertFailsOn(PagedQuery query, string propertyName)
    {
        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == propertyName);
    }

    private sealed class TestQueryValidator : PagedQueryValidator<PagedQuery>
    {
        public TestQueryValidator()
            : base(SortFields)
        {
        }
    }

    private sealed class EmptySortFieldsValidator : PagedQueryValidator<PagedQuery>
    {
        public EmptySortFieldsValidator()
            : base([])
        {
        }
    }
}
