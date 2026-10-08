using LocalMateAI.Application.DTOs.Search;
using LocalMateAI.Application.Validators.Search;

namespace LocalMateAI.Tests;

public sealed class SearchQueryValidatorTests
{
    private readonly SearchQueryValidator validator = new();

    [Theory]
    [InlineData("ab")]
    [InlineData("  bến   thành  ")]
    public void KeywordWithinLimits_IsValid(string q)
    {
        Assert.True(validator.Validate(new SearchQuery { Q = q }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("a")]
    [InlineData("   a   ")]
    public void MissingOrTooShortKeyword_FailsOnQ(string? q)
    {
        var result = validator.Validate(new SearchQuery { Q = q });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(SearchQuery.Q), error.PropertyName);
        Assert.Equal($"Từ khoá phải có ít nhất {SearchQuery.MinKeywordLength} ký tự.", error.ErrorMessage);
    }

    [Fact]
    public void KeywordLongerThanLimit_FailsOnQ()
    {
        var atLimit = new string('a', SearchQuery.MaxKeywordLength);

        var result = validator.Validate(new SearchQuery { Q = atLimit + "a" });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(SearchQuery.Q), error.PropertyName);
        Assert.Equal($"Từ khoá tối đa {SearchQuery.MaxKeywordLength} ký tự.", error.ErrorMessage);
        Assert.True(validator.Validate(new SearchQuery { Q = atLimit }).IsValid);
    }

    [Fact]
    public void Keyword_TrimsAndCollapsesWhitespace_BeforeLengthIsChecked()
    {
        Assert.Equal("bến thành", new SearchQuery { Q = "  bến \t  thành \n" }.Keyword);
        Assert.Null(new SearchQuery { Q = "   " }.Keyword);
        Assert.Null(new SearchQuery().Keyword);

        // 2 chữ cách nhau 200 khoảng trắng: sau khi gộp chỉ còn 3 ký tự nên hợp lệ.
        Assert.True(validator.Validate(new SearchQuery { Q = "a" + new string(' ', 200) + "b" }).IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(SearchQuery.MaxLimit)]
    public void LimitWithinRange_IsValid(int limit)
    {
        Assert.True(validator.Validate(new SearchQuery { Q = "ga", Limit = limit }).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SearchQuery.MaxLimit + 1)]
    public void LimitOutOfRange_FailsOnLimit(int limit)
    {
        var result = validator.Validate(new SearchQuery { Q = "ga", Limit = limit });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(SearchQuery.Limit), error.PropertyName);
    }

    [Fact]
    public void DefaultLimit_IsFive()
    {
        Assert.Equal(5, new SearchQuery { Q = "ga" }.Limit);
    }
}
