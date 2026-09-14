namespace LocalMateAI.Application.DTOs.Auth;

public enum LoginResultStatus
{
    Success,
    ValidationFailed,
    InvalidCredentials
}

public sealed record LoginResult
{
    private LoginResult(
        LoginResultStatus status,
        LoginResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
    }

    public LoginResultStatus Status { get; }

    public LoginResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public static LoginResult Succeeded(LoginResponse response) =>
        new(LoginResultStatus.Success, response);

    public static LoginResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(LoginResultStatus.ValidationFailed, validationErrors: validationErrors);

    public static LoginResult InvalidCredentials() =>
        new(LoginResultStatus.InvalidCredentials);
}
