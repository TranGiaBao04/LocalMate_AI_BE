namespace LocalMateAI.Application.DTOs.Auth;

public enum VerifyRegistrationResultStatus
{
    Success,
    ValidationFailed,
    InvalidOtp,
    OtpExpired,
    OtpAttemptsExceeded,
    DuplicateEmail
}

public sealed class VerifyRegistrationResult
{
    private VerifyRegistrationResult(
        VerifyRegistrationResultStatus status,
        RegisterResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
        int? remainingAttempts = null)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
        RemainingAttempts = remainingAttempts;
    }

    public VerifyRegistrationResultStatus Status { get; }

    public RegisterResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public int? RemainingAttempts { get; }

    public static VerifyRegistrationResult Succeeded(RegisterResponse response) =>
        new(VerifyRegistrationResultStatus.Success, response);

    public static VerifyRegistrationResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(VerifyRegistrationResultStatus.ValidationFailed, validationErrors: validationErrors);

    public static VerifyRegistrationResult InvalidOtp(int? remainingAttempts = null) =>
        new(VerifyRegistrationResultStatus.InvalidOtp, remainingAttempts: remainingAttempts);

    public static VerifyRegistrationResult OtpExpired() =>
        new(VerifyRegistrationResultStatus.OtpExpired);

    public static VerifyRegistrationResult OtpAttemptsExceeded() =>
        new(VerifyRegistrationResultStatus.OtpAttemptsExceeded);

    public static VerifyRegistrationResult EmailAlreadyExists() =>
        new(VerifyRegistrationResultStatus.DuplicateEmail);
}
