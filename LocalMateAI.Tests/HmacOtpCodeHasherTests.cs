using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class HmacOtpCodeHasherTests
{
    private const string Email = "user@example.com";

    // 32 byte ngẫu nhiên, chỉ dùng cho test.
    private const string HashKey = "q1XZ3r0l8Yb7Vd2Hk9Tn4Pw6Jc5Mf0Ga1Se3Ru7Lo8I=";
    private const string OtherHashKey = "Zm9vYmFyYmF6cXV4cXV1eGNvcmdlZ3JhdWx0Z2FycGx5Zg==";

    [Fact]
    public void Hash_ThenVerifySameInputs_ReturnsTrue()
    {
        var hasher = CreateHasher(HashKey);

        var codeHash = hasher.Hash(Email, OtpPurpose.Registration, "123456");

        Assert.True(hasher.Verify(Email, OtpPurpose.Registration, "123456", codeHash));
    }

    [Fact]
    public void Hash_ReturnsHexOfSha256WithoutPlainCode()
    {
        var codeHash = CreateHasher(HashKey).Hash(Email, OtpPurpose.Registration, "123456");

        Assert.Matches("^[0-9A-F]{64}$", codeHash);
        Assert.DoesNotContain("123456", codeHash);
    }

    [Theory]
    [InlineData("user@example.com", OtpPurpose.Registration, "654321")]
    [InlineData("other@example.com", OtpPurpose.Registration, "123456")]
    [InlineData("user@example.com", OtpPurpose.PasswordReset, "123456")]
    public void Verify_DifferentCodeEmailOrPurpose_ReturnsFalse(string email, OtpPurpose purpose, string code)
    {
        var hasher = CreateHasher(HashKey);
        var codeHash = hasher.Hash(Email, OtpPurpose.Registration, "123456");

        Assert.False(hasher.Verify(email, purpose, code, codeHash));
    }

    [Fact]
    public void Hash_DifferentKeys_ProduceDifferentHashes()
    {
        var first = CreateHasher(HashKey).Hash(Email, OtpPurpose.Registration, "123456");
        var second = CreateHasher(OtherHashKey).Hash(Email, OtpPurpose.Registration, "123456");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("ABCD")]
    public void Verify_MalformedStoredHash_ReturnsFalse(string codeHash)
    {
        Assert.False(CreateHasher(HashKey).Verify(Email, OtpPurpose.Registration, "123456", codeHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("c2hvcnQ=")]
    public void Hash_InvalidHashKey_Throws(string hashKey)
    {
        var hasher = CreateHasher(hashKey);

        Assert.Throws<InvalidOperationException>(() => hasher.Hash(Email, OtpPurpose.Registration, "123456"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("c2hvcnQ=", false)]
    [InlineData(HashKey, true)]
    public void OptionsValidator_RequiresBase64KeyOfAtLeast32Bytes(string hashKey, bool expectedValid)
    {
        var result = new OtpOptionsValidator().Validate(null, new OtpOptions { HashKey = hashKey });

        Assert.Equal(expectedValid, result.Succeeded);
    }

    private static HmacOtpCodeHasher CreateHasher(string hashKey) =>
        new(Options.Create(new OtpOptions { HashKey = hashKey }));
}
