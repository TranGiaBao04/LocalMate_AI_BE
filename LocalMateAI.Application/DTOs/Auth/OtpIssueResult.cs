namespace LocalMateAI.Application.DTOs.Auth;

public enum OtpIssueStatus
{
    Sent,
    Cooldown,
    RateLimited
}

public sealed record OtpIssueResult(OtpIssueStatus Status, int RetryAfterSeconds = 0)
{
    public static OtpIssueResult Sent() => new(OtpIssueStatus.Sent);

    public static OtpIssueResult Cooldown(int retryAfterSeconds) =>
        new(OtpIssueStatus.Cooldown, retryAfterSeconds);

    public static OtpIssueResult RateLimited(int retryAfterSeconds) =>
        new(OtpIssueStatus.RateLimited, retryAfterSeconds);
}
