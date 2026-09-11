using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Security;

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private const int MinimumSigningKeyBytes = 32;
    private const int MaximumAccessTokenMinutes = 1_440;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is required.");
        }

        if (!TryDecodeSigningKey(options.SigningKey, out _))
        {
            failures.Add("Jwt:SigningKey must be valid Base64 and decode to at least 32 bytes.");
        }

        if (options.AccessTokenMinutes is <= 0 or > MaximumAccessTokenMinutes)
        {
            failures.Add("Jwt:AccessTokenMinutes must be between 1 and 1440.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    public static bool TryDecodeSigningKey(string signingKey, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(signingKey);
            return bytes.Length >= MinimumSigningKeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
