using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class EmailOtpServiceTests
{
    private const string Email = "user@example.com";

    private static readonly DateTime Now = new(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Issue_NoRecentSends_SavesHashedCodeAndSendsEmail()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.IssueAsync(Email, OtpPurpose.Registration);

        Assert.Equal(OtpIssueStatus.Sent, result.Status);
        var saved = Assert.Single(fixture.Repository.Saved);
        Assert.Equal(Email, saved.Email);
        Assert.Equal(OtpPurpose.Registration, saved.Purpose);
        Assert.Equal(Now + EmailOtpService.CodeLifetime, saved.ExpiresAt);

        var sent = Assert.Single(fixture.Sender.Sent);
        Assert.Equal(Email, sent.ToEmail);
        Assert.Equal("Mã xác thực đăng ký LocalMate AI", sent.Subject);

        var rendered = Assert.Single(fixture.Renderer.Rendered);
        Assert.Equal(EmailTemplateNames.OtpRegistration, rendered.TemplateName);
        var model = Assert.IsType<OtpEmailModel>(rendered.Model);
        Assert.Matches("^[0-9]{6}$", model.Code);
        Assert.Equal(10, model.ExpiresInMinutes);

        // Chỉ lưu bản băm, không lưu mã gốc.
        Assert.NotEqual(model.Code, saved.CodeHash);
        Assert.Equal(FakeHasher.HashOf(Email, OtpPurpose.Registration, model.Code), saved.CodeHash);
    }

    [Fact]
    public async Task Issue_PasswordReset_UsesPasswordResetTemplateAndSubject()
    {
        var fixture = new Fixture();

        await fixture.Service.IssueAsync(Email, OtpPurpose.PasswordReset);

        Assert.Equal(EmailTemplateNames.OtpPasswordReset, Assert.Single(fixture.Renderer.Rendered).TemplateName);
        Assert.Equal("Mã đặt lại mật khẩu LocalMate AI", Assert.Single(fixture.Sender.Sent).Subject);
    }

    [Fact]
    public async Task Issue_LastSendWithinCooldown_ReturnsCooldownWithoutSavingOrSending()
    {
        var fixture = new Fixture();
        fixture.Repository.CreatedTimes.Add(Now.AddSeconds(-20));

        var result = await fixture.Service.IssueAsync(Email, OtpPurpose.Registration);

        Assert.Equal(OtpIssueStatus.Cooldown, result.Status);
        Assert.Equal(40, result.RetryAfterSeconds);
        Assert.Empty(fixture.Repository.Saved);
        Assert.Empty(fixture.Sender.Sent);
    }

    [Fact]
    public async Task Issue_LastSendExactlyAtCooldownEnd_IsAllowed()
    {
        var fixture = new Fixture();
        fixture.Repository.CreatedTimes.Add(Now - EmailOtpService.ResendCooldown);

        var result = await fixture.Service.IssueAsync(Email, OtpPurpose.Registration);

        Assert.Equal(OtpIssueStatus.Sent, result.Status);
    }

    [Fact]
    public async Task Issue_FiveSendsInLastHour_ReturnsRateLimitedUntilOldestLeavesWindow()
    {
        var fixture = new Fixture();
        fixture.Repository.CreatedTimes.AddRange(
        [
            Now.AddMinutes(-55),
            Now.AddMinutes(-40),
            Now.AddMinutes(-30),
            Now.AddMinutes(-20),
            Now.AddMinutes(-5)
        ]);

        var result = await fixture.Service.IssueAsync(Email, OtpPurpose.Registration);

        Assert.Equal(OtpIssueStatus.RateLimited, result.Status);
        Assert.Equal(5 * 60, result.RetryAfterSeconds);
        Assert.Empty(fixture.Repository.Saved);
        Assert.Empty(fixture.Sender.Sent);
    }

    [Fact]
    public async Task Issue_FourSendsInLastHour_IsAllowed()
    {
        var fixture = new Fixture();
        fixture.Repository.CreatedTimes.AddRange(
        [
            Now.AddMinutes(-40),
            Now.AddMinutes(-30),
            Now.AddMinutes(-20),
            Now.AddMinutes(-5)
        ]);

        var result = await fixture.Service.IssueAsync(Email, OtpPurpose.Registration);

        Assert.Equal(OtpIssueStatus.Sent, result.Status);
    }

    [Fact]
    public async Task Issue_ConcurrentRequestCreatedCodeFirst_ReturnsCooldownWithoutSending()
    {
        var fixture = new Fixture();
        fixture.Repository.ReplaceSucceeds = false;

        var result = await fixture.Service.IssueAsync(Email, OtpPurpose.Registration);

        Assert.Equal(OtpIssueStatus.Cooldown, result.Status);
        Assert.Equal(60, result.RetryAfterSeconds);
        Assert.Empty(fixture.Sender.Sent);
    }

    [Fact]
    public async Task Issue_EmailSendFails_StillReturnsSent()
    {
        var fixture = new Fixture();
        fixture.Sender.Succeeds = false;

        var result = await fixture.Service.IssueAsync(Email, OtpPurpose.Registration);

        Assert.Equal(OtpIssueStatus.Sent, result.Status);
        Assert.Single(fixture.Repository.Saved);
    }

    [Fact]
    public async Task Verify_NoActiveCode_ReturnsInvalid()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "123456");

        Assert.Equal(OtpVerifyStatus.Invalid, result.Status);
        Assert.Null(result.RemainingAttempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Verify_ExpiredCode_ReturnsExpired(int minutesUntilExpiry)
    {
        var fixture = new Fixture();
        fixture.Repository.Active = ActiveCode("123456", expiresAt: Now.AddMinutes(minutesUntilExpiry));

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "123456");

        Assert.Equal(OtpVerifyStatus.Expired, result.Status);
        Assert.Equal(0, fixture.Repository.ConsumeCalls);
    }

    [Fact]
    public async Task Verify_AttemptsAlreadyUsedUp_ReturnsAttemptsExceededWithoutCounting()
    {
        var fixture = new Fixture();
        fixture.Repository.Active = ActiveCode("123456", attemptCount: EmailOtpService.MaxAttempts);

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "123456");

        Assert.Equal(OtpVerifyStatus.AttemptsExceeded, result.Status);
        Assert.Equal(0, fixture.Repository.IncrementCalls);
        Assert.Equal(0, fixture.Repository.ConsumeCalls);
    }

    [Fact]
    public async Task Verify_WrongCode_CountsAttemptAndReturnsRemaining()
    {
        var fixture = new Fixture();
        fixture.Repository.Active = ActiveCode("123456", attemptCount: 1);

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "654321");

        Assert.Equal(OtpVerifyStatus.Invalid, result.Status);
        Assert.Equal(3, result.RemainingAttempts);
        Assert.Equal(1, fixture.Repository.IncrementCalls);
        Assert.Equal(0, fixture.Repository.ConsumeCalls);
    }

    [Fact]
    public async Task Verify_WrongCodeOnLastAttempt_ReturnsAttemptsExceeded()
    {
        var fixture = new Fixture();
        fixture.Repository.Active = ActiveCode("123456", attemptCount: EmailOtpService.MaxAttempts - 1);

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "654321");

        Assert.Equal(OtpVerifyStatus.AttemptsExceeded, result.Status);
        Assert.Equal(1, fixture.Repository.IncrementCalls);
    }

    [Fact]
    public async Task Verify_WrongCodeWhenCountingRejected_ReturnsAttemptsExceeded()
    {
        var fixture = new Fixture();
        fixture.Repository.Active = ActiveCode("123456", attemptCount: 2);
        fixture.Repository.IncrementSucceeds = false;

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "654321");

        Assert.Equal(OtpVerifyStatus.AttemptsExceeded, result.Status);
    }

    [Fact]
    public async Task Verify_CodeOfOtherPurpose_IsRejected()
    {
        var fixture = new Fixture();
        // Mã được băm cho PasswordReset nhưng đem đi xác thực đăng ký.
        fixture.Repository.Active = ActiveCode("123456", purpose: OtpPurpose.PasswordReset);

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "123456");

        Assert.Equal(OtpVerifyStatus.Invalid, result.Status);
        Assert.Equal(0, fixture.Repository.ConsumeCalls);
    }

    [Fact]
    public async Task Verify_CorrectCode_ConsumesAndReturnsSuccess()
    {
        var fixture = new Fixture();
        var active = ActiveCode("123456", attemptCount: 2);
        fixture.Repository.Active = active;

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "123456");

        Assert.Equal(OtpVerifyStatus.Success, result.Status);
        Assert.Equal(active.Id, fixture.Repository.ConsumedId);
        Assert.Equal(0, fixture.Repository.IncrementCalls);
    }

    [Fact]
    public async Task Verify_CorrectCodeUsedByConcurrentRequest_ReturnsInvalid()
    {
        var fixture = new Fixture();
        fixture.Repository.Active = ActiveCode("123456");
        fixture.Repository.ConsumeSucceeds = false;

        var result = await fixture.Service.VerifyAsync(Email, OtpPurpose.Registration, "123456");

        Assert.Equal(OtpVerifyStatus.Invalid, result.Status);
    }

    private static EmailOtpCode ActiveCode(
        string code,
        int attemptCount = 0,
        DateTime? expiresAt = null,
        OtpPurpose purpose = OtpPurpose.Registration) => new()
    {
        Email = Email,
        Purpose = OtpPurpose.Registration,
        CodeHash = FakeHasher.HashOf(Email, purpose, code),
        ExpiresAt = expiresAt ?? Now.AddMinutes(5),
        AttemptCount = attemptCount
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            Service = new EmailOtpService(
                Repository,
                new FakeHasher(),
                Renderer,
                Sender,
                new FixedTimeProvider(new DateTimeOffset(Now)));
        }

        public FakeOtpRepository Repository { get; } = new();
        public FakeRenderer Renderer { get; } = new();
        public FakeSender Sender { get; } = new();
        public EmailOtpService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeHasher : IOtpCodeHasher
    {
        public static string HashOf(string email, OtpPurpose purpose, string code) => $"hash:{email}:{purpose}:{code}";

        public string Hash(string email, OtpPurpose purpose, string code) => HashOf(email, purpose, code);

        public bool Verify(string email, OtpPurpose purpose, string code, string codeHash) =>
            HashOf(email, purpose, code) == codeHash;
    }

    private sealed class FakeRenderer : IEmailTemplateRenderer
    {
        public List<(string TemplateName, object Model)> Rendered { get; } = [];

        public Task<string> RenderAsync(string templateName, object model, CancellationToken cancellationToken = default)
        {
            Rendered.Add((templateName, model));
            return Task.FromResult($"<p>{templateName}</p>");
        }
    }

    private sealed class FakeSender : IEmailSender
    {
        public bool Succeeds { get; set; } = true;
        public List<(string ToEmail, string Subject, string HtmlBody)> Sent { get; } = [];

        public Task<bool> TrySendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((toEmail, subject, htmlBody));
            return Task.FromResult(Succeeds);
        }
    }

    private sealed class FakeOtpRepository : IEmailOtpCodeRepository
    {
        public List<DateTime> CreatedTimes { get; } = [];
        public EmailOtpCode? Active { get; set; }
        public List<EmailOtpCode> Saved { get; } = [];
        public bool ReplaceSucceeds { get; set; } = true;
        public bool IncrementSucceeds { get; set; } = true;
        public bool ConsumeSucceeds { get; set; } = true;
        public int IncrementCalls { get; private set; }
        public int ConsumeCalls { get; private set; }
        public Guid? ConsumedId { get; private set; }

        public Task<IReadOnlyList<DateTime>> GetCreatedTimesSinceAsync(
            string email,
            OtpPurpose purpose,
            DateTime since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DateTime>>(CreatedTimes.Where(time => time >= since).Order().ToList());

        public Task<EmailOtpCode?> GetActiveAsync(
            string email,
            OtpPurpose purpose,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Active);

        public Task<bool> TryReplaceActiveAsync(
            EmailOtpCode otpCode,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            if (ReplaceSucceeds)
            {
                Saved.Add(otpCode);
            }

            return Task.FromResult(ReplaceSucceeds);
        }

        public Task<bool> TryIncrementAttemptAsync(
            Guid otpCodeId,
            int maxAttempts,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            IncrementCalls++;
            return Task.FromResult(IncrementSucceeds);
        }

        public Task<bool> TryConsumeAsync(
            Guid otpCodeId,
            int maxAttempts,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            ConsumeCalls++;
            if (ConsumeSucceeds)
            {
                ConsumedId = otpCodeId;
            }

            return Task.FromResult(ConsumeSucceeds);
        }
    }
}
