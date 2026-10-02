namespace LocalMateAI.Infrastructure.Security;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    public string HashKey { get; init; } = string.Empty;
}
