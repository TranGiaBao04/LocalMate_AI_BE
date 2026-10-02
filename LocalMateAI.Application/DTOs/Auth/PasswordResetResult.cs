namespace LocalMateAI.Application.DTOs.Auth;

public enum PasswordResetResultStatus
{
    Success,
    ValidationFailed,
    InvalidOtp,
    OtpExpired,
    OtpAttemptsExceeded
}

public sealed class PasswordResetResult
{
    private PasswordResetResult(
        PasswordResetResultStatus status,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
        int? remainingAttempts = null)
    {
        Status = status;
        ValidationErrors = validationErrors;
        RemainingAttempts = remainingAttempts;
    }

    public PasswordResetResultStatus Status { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public int? RemainingAttempts { get; }

    public static PasswordResetResult Succeeded() =>
        new(PasswordResetResultStatus.Success);

    public static PasswordResetResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(PasswordResetResultStatus.ValidationFailed, validationErrors);

    public static PasswordResetResult InvalidOtp(int? remainingAttempts = null) =>
        new(PasswordResetResultStatus.InvalidOtp, remainingAttempts: remainingAttempts);

    public static PasswordResetResult OtpExpired() =>
        new(PasswordResetResultStatus.OtpExpired);

    public static PasswordResetResult OtpAttemptsExceeded() =>
        new(PasswordResetResultStatus.OtpAttemptsExceeded);
}
