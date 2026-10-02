using System.Text.RegularExpressions;
using LocalMateAI.Application.Security;

namespace LocalMateAI.Tests;

public sealed class TokenGeneratorTests
{
    private static readonly Regex UrlSafeRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    [Fact]
    public void GenerateShareToken_Default128Bit_Is22CharsWithoutPadding()
    {
        var token = TokenGenerator.GenerateShareToken();

        Assert.Equal(22, token.Length); // 16 bytes = 128 bit → ⌈128/6⌉ = 22 ký tự Base64URL
        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    [Fact]
    public void GenerateShareToken_IsUrlSafe()
    {
        var token = TokenGenerator.GenerateShareToken();

        Assert.Matches(UrlSafeRegex, token);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    [InlineData(32)]
    public void GenerateShareToken_RespectsByteLength(int bytes)
    {
        var expectedLength = (int)Math.Ceiling(bytes * 8.0 / 6.0);
        var token = TokenGenerator.GenerateShareToken(bytes);

        Assert.Equal(expectedLength, token.Length);
    }

    [Fact]
    public void GenerateShareToken_ProducesDistinctTokens()
    {
        var tokens = Enumerable.Range(0, 1000)
            .Select(_ => TokenGenerator.GenerateShareToken())
            .ToArray();

        Assert.Equal(1000, tokens.Distinct().Count()); // không trùng lặp
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GenerateShareToken_InvalidByteLength_Throws(int byteLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TokenGenerator.GenerateShareToken(byteLength));
    }
}