using LocalMateAI.Application.Security;

namespace LocalMateAI.Tests;

public sealed class PasswordRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("        ")]
    public void GetError_InvalidPassword_ReturnsError(string? password)
    {
        Assert.NotNull(PasswordRules.GetError(password));
    }

    [Fact]
    public void GetError_TooLong_ReturnsError()
    {
        Assert.NotNull(PasswordRules.GetError(new string('a', PasswordRules.MaximumLength + 1)));
    }

    [Theory]
    [InlineData(PasswordRules.MinimumLength)]
    [InlineData(PasswordRules.MaximumLength)]
    public void GetError_LengthAtBoundary_IsValid(int length)
    {
        Assert.Null(PasswordRules.GetError(new string('a', length)));
    }
}
