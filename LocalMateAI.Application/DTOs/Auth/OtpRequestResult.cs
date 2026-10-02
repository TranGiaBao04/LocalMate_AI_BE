namespace LocalMateAI.Application.DTOs.Auth;

public enum OtpRequestResultStatus
{
    Accepted,
    ValidationFailed,
    Cooldown,
    RateLimited,
    GoogleAccountWithoutPassword
}

public sealed class OtpRequestResult
{
    private OtpRequestResult(
        OtpRequestResultStatus status,
        OtpDispatchResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
        int retryAfterSeconds = 0)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public OtpRequestResultStatus Status { get; }

    public OtpDispatchResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public int RetryAfterSeconds { get; }

    public static OtpRequestResult Accepted(OtpDispatchResponse response) =>
        new(OtpRequestResultStatus.Accepted, response);

    public static OtpRequestResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(OtpRequestResultStatus.ValidationFailed, validationErrors: validationErrors);

    public static OtpRequestResult Cooldown(int retryAfterSeconds) =>
        new(OtpRequestResultStatus.Cooldown, retryAfterSeconds: retryAfterSeconds);

    public static OtpRequestResult RateLimited(int retryAfterSeconds) =>
        new(OtpRequestResultStatus.RateLimited, retryAfterSeconds: retryAfterSeconds);

    public static OtpRequestResult GoogleAccountWithoutPassword() =>
        new(OtpRequestResultStatus.GoogleAccountWithoutPassword);
}
