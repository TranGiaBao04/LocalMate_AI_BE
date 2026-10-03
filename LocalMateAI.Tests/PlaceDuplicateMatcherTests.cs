using LocalMateAI.Application.Services;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class PlaceDuplicateMatcherTests
{
    [Theory]
    [InlineData("Quán Cà Phê A", "quán cà phê a")]
    [InlineData("Highlands Coffee", "highlands coffee")]
    [InlineData("Phở Hòa", "PHỞ HÒA")]
    public void IsSimilarName_ExactIgnoreCase_ReturnsTrue(string candidate, string input)
    {
        Assert.True(PlaceDuplicateMatcher.IsSimilarName(candidate, input));
    }

    [Theory]
    [InlineData("Quán Cà Phê A Nguyễn Huệ", "cà phê a")]
    [InlineData("Bún Bò Huế", "bún bò")]
    public void IsSimilarName_Containment_ReturnsTrue(string candidate, string input)
    {
        Assert.True(PlaceDuplicateMatcher.IsSimilarName(candidate, input));
    }

    [Theory]
    [InlineData("Quán Cà Phê A", "Nhà Hàng B")]
    [InlineData("Highlands", "Starbucks")]
    public void IsSimilarName_DifferentNames_ReturnsFalse(string candidate, string input)
    {
        Assert.False(PlaceDuplicateMatcher.IsSimilarName(candidate, input));
    }

    [Theory]
    [InlineData("ab", "ab")]
    [InlineData("", "cà phê")]
    [InlineData(null, "cà phê")]
    public void IsSimilarName_TooShort_ReturnsFalse(string? candidate, string input)
    {
        Assert.False(PlaceDuplicateMatcher.IsSimilarName(candidate, input));
    }
}
