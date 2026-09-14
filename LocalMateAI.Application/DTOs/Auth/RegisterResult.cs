namespace LocalMateAI.Application.DTOs.Auth;

public enum RegisterResultStatus
{
    Success,
    ValidationFailed,
    DuplicateEmail
}

public sealed class RegisterResult
{
    private RegisterResult(
        RegisterResultStatus status,
        RegisterResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
    }

    public RegisterResultStatus Status { get; }

    public RegisterResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public static RegisterResult Succeeded(RegisterResponse response) =>
        new(RegisterResultStatus.Success, response);

    public static RegisterResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(RegisterResultStatus.ValidationFailed, validationErrors: validationErrors);

    public static RegisterResult EmailAlreadyExists() =>
        new(RegisterResultStatus.DuplicateEmail);
}
