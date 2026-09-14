using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Security;

public sealed class GoogleAuthOptionsValidator : IValidateOptions<GoogleAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleAuthOptions options) =>
        string.IsNullOrWhiteSpace(options.ClientId)
            ? ValidateOptionsResult.Fail("GoogleAuth:ClientId is required.")
            : ValidateOptionsResult.Success;
}
