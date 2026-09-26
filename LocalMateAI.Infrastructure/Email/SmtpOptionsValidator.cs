using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Email;

public sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("Smtp:Host is required.");
        }

        if (options.Port is <= 0 or > 65_535)
        {
            failures.Add("Smtp:Port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            failures.Add("Smtp:Username is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            failures.Add("Smtp:Password is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
