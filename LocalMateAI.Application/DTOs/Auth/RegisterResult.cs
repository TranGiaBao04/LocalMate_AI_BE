namespace LocalMateAI.Application.DTOs.Auth;

public enum RegisterResultStatus
{
    Success,
    ValidationFailed,
    DuplicateEmail,
    Cooldown,
    RateLimited
}

public sealed class RegisterResult
{
    private RegisterResult(
        RegisterResultStatus status,
        OtpDispatchResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
        int retryAfterSeconds = 0)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public RegisterResultStatus Status { get; }

    public OtpDispatchResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public int RetryAfterSeconds { get; }

    public static RegisterResult Succeeded(OtpDispatchResponse response) =>
        new(RegisterResultStatus.Success, response);

    public static RegisterResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(RegisterResultStatus.ValidationFailed, validationErrors: validationErrors);

    public static RegisterResult EmailAlreadyExists() =>
        new(RegisterResultStatus.DuplicateEmail);

    public static RegisterResult Cooldown(int retryAfterSeconds) =>
        new(RegisterResultStatus.Cooldown, retryAfterSeconds: retryAfterSeconds);

    public static RegisterResult RateLimited(int retryAfterSeconds) =>
        new(RegisterResultStatus.RateLimited, retryAfterSeconds: retryAfterSeconds);
}
