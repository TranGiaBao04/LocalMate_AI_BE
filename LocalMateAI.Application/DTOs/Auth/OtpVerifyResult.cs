namespace LocalMateAI.Application.DTOs.Auth;

public enum OtpVerifyStatus
{
    Success,
    Invalid,
    Expired,
    AttemptsExceeded
}

public sealed record OtpVerifyResult(OtpVerifyStatus Status, int? RemainingAttempts = null)
{
    public static OtpVerifyResult Success() => new(OtpVerifyStatus.Success);

    public static OtpVerifyResult Invalid(int? remainingAttempts = null) =>
        new(OtpVerifyStatus.Invalid, remainingAttempts);

    public static OtpVerifyResult Expired() => new(OtpVerifyStatus.Expired);

    public static OtpVerifyResult AttemptsExceeded() => new(OtpVerifyStatus.AttemptsExceeded);
}
