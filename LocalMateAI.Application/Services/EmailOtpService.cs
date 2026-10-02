using System.Globalization;
using System.Security.Cryptography;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class EmailOtpService(
    IEmailOtpCodeRepository otpCodeRepository,
    IOtpCodeHasher otpCodeHasher,
    IEmailTemplateRenderer emailTemplateRenderer,
    IEmailSender emailSender,
    TimeProvider timeProvider) : IEmailOtpService
{
    public const int MaxAttempts = 5;
    public const int MaxSendsPerHour = 5;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);

    public async Task<OtpIssueResult> IssueAsync(
        string email,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var recentSends = await otpCodeRepository.GetCreatedTimesSinceAsync(
            email,
            purpose,
            now - SendWindow,
            cancellationToken);

        if (recentSends.Count > 0)
        {
            var cooldownEndsAt = recentSends[^1] + ResendCooldown;
            if (cooldownEndsAt > now)
            {
                return OtpIssueResult.Cooldown(SecondsUntil(cooldownEndsAt, now));
            }
        }

        if (recentSends.Count >= MaxSendsPerHour)
        {
            // Phải đợi tới khi mã thứ MaxSendsPerHour tính từ cuối rời khỏi cửa sổ 1 giờ.
            var windowFreesAt = recentSends[^MaxSendsPerHour] + SendWindow;
            return OtpIssueResult.RateLimited(SecondsUntil(windowFreesAt, now));
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var (templateName, subject) = EmailContentFor(purpose);

        // Dựng nội dung trước khi lưu mã: template lỗi thì không để lại mã không ai nhận được trong DB.
        var htmlBody = await emailTemplateRenderer.RenderAsync(
            templateName,
            new OtpEmailModel(code, (int)CodeLifetime.TotalMinutes),
            cancellationToken);

        var otpCode = new EmailOtpCode
        {
            Email = email,
            Purpose = purpose,
            CodeHash = otpCodeHasher.Hash(email, purpose, code),
            ExpiresAt = now + CodeLifetime
        };

        var saved = await otpCodeRepository.TryReplaceActiveAsync(otpCode, now, cancellationToken);
        if (!saved)
        {
            // Một request khác vừa tạo mã cho cùng email: coi như đang trong thời gian chờ.
            return OtpIssueResult.Cooldown((int)ResendCooldown.TotalSeconds);
        }

        // Gửi lỗi vẫn trả Sent: lỗi đã được ghi log, user bấm "Gửi lại" sau 60 giây.
        await emailSender.TrySendAsync(email, subject, htmlBody, cancellationToken);

        return OtpIssueResult.Sent();
    }

    public async Task<OtpVerifyResult> VerifyAsync(
        string email,
        OtpPurpose purpose,
        string code,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var activeCode = await otpCodeRepository.GetActiveAsync(email, purpose, cancellationToken);
        if (activeCode is null)
        {
            return OtpVerifyResult.Invalid();
        }

        if (activeCode.ExpiresAt <= now)
        {
            return OtpVerifyResult.Expired();
        }

        if (activeCode.AttemptCount >= MaxAttempts)
        {
            return OtpVerifyResult.AttemptsExceeded();
        }

        if (!otpCodeHasher.Verify(email, purpose, code, activeCode.CodeHash))
        {
            var counted = await otpCodeRepository.TryIncrementAttemptAsync(
                activeCode.Id,
                MaxAttempts,
                now,
                cancellationToken);
            if (!counted)
            {
                return OtpVerifyResult.AttemptsExceeded();
            }

            var remainingAttempts = MaxAttempts - (activeCode.AttemptCount + 1);
            return remainingAttempts <= 0
                ? OtpVerifyResult.AttemptsExceeded()
                : OtpVerifyResult.Invalid(remainingAttempts);
        }

        // Điều kiện trong câu UPDATE chặn việc 2 request cùng dùng 1 mã đúng.
        var consumed = await otpCodeRepository.TryConsumeAsync(
            activeCode.Id,
            MaxAttempts,
            now,
            cancellationToken);

        return consumed ? OtpVerifyResult.Success() : OtpVerifyResult.Invalid();
    }

    private static int SecondsUntil(DateTime target, DateTime now) =>
        Math.Max(1, (int)Math.Ceiling((target - now).TotalSeconds));

    private static (string TemplateName, string Subject) EmailContentFor(OtpPurpose purpose) =>
        purpose switch
        {
            OtpPurpose.Registration => (
                EmailTemplateNames.OtpRegistration,
                "Mã xác thực đăng ký LocalMate AI"),
            OtpPurpose.PasswordReset => (
                EmailTemplateNames.OtpPasswordReset,
                "Mã đặt lại mật khẩu LocalMate AI"),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
        };
}
