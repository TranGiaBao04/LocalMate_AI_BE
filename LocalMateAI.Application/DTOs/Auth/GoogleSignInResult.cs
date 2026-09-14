namespace LocalMateAI.Application.DTOs.Auth;

public enum GoogleSignInResultStatus
{
    Success,
    ValidationFailed,
    InvalidGoogleToken,
    AccountLinkRequired,
    AccountConflict
}

public sealed record GoogleSignInResult
{
    private GoogleSignInResult(
        GoogleSignInResultStatus status,
        LoginResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
    }

    public GoogleSignInResultStatus Status { get; }

    public LoginResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public static GoogleSignInResult Succeeded(LoginResponse response) =>
        new(GoogleSignInResultStatus.Success, response);

    public static GoogleSignInResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(GoogleSignInResultStatus.ValidationFailed, validationErrors: validationErrors);

    public static GoogleSignInResult InvalidGoogleToken() =>
        new(GoogleSignInResultStatus.InvalidGoogleToken);

    public static GoogleSignInResult AccountLinkRequired() =>
        new(GoogleSignInResultStatus.AccountLinkRequired);

    public static GoogleSignInResult AccountConflict() =>
        new(GoogleSignInResultStatus.AccountConflict);
}
