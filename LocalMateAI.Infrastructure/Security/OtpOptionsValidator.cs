using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Security;

public sealed class OtpOptionsValidator : IValidateOptions<OtpOptions>
{
    private const int MinimumHashKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, OtpOptions options) =>
        TryDecodeHashKey(options.HashKey, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Otp:HashKey must be valid Base64 and decode to at least 32 bytes.");

    public static bool TryDecodeHashKey(string hashKey, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrWhiteSpace(hashKey))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(hashKey);
            return bytes.Length >= MinimumHashKeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
