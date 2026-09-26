using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AuthServicePasswordResetTests
{
    private const string Email = "user@example.com";
    private const string OldPassword = "OldSecret123";
    private const string NewPassword = "NewSecret456";

    private static readonly DateTime Now = new(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RequestPasswordReset_UnknownEmail_ReturnsAcceptedWithoutSending()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestPasswordResetAsync(new RequestPasswordResetRequest { Email = Email });

        Assert.Equal(OtpRequestResultStatus.Accepted, result.Status);
        Assert.Equal(new OtpDispatchResponse(Email, 600, 60), result.Response);
        Assert.Empty(fixture.Otp.Issued);
    }

    [Fact]
    public async Task RequestPasswordReset_GoogleOnlyAccount_ReturnsGoogleAccountWithoutSending()
    {
        var fixture = new Fixture();
        fixture.Users.Existing = GoogleOnlyUser();

        var result = await fixture.Service.RequestPasswordResetAsync(new RequestPasswordResetRequest { Email = Email });

        Assert.Equal(OtpRequestResultStatus.GoogleAccountWithoutPassword, result.Status);
        Assert.Empty(fixture.Otp.Issued);
    }

    [Fact]
    public async Task RequestPasswordReset_PasswordAccount_IssuesPasswordResetCode()
    {
        var fixture = new Fixture();
        fixture.Users.Existing = PasswordUser();

        var result = await fixture.Service.RequestPasswordResetAsync(
            new RequestPasswordResetRequest { Email = "  User@Example.COM " });

        Assert.Equal(OtpRequestResultStatus.Accepted, result.Status);
        Assert.Equal(new OtpDispatchResponse(Email, 600, 60), result.Response);
        Assert.Equal((Email, OtpPurpose.PasswordReset), Assert.Single(fixture.Otp.Issued));
    }

    [Theory]
    [InlineData(OtpIssueStatus.Cooldown, OtpRequestResultStatus.Cooldown)]
    [InlineData(OtpIssueStatus.RateLimited, OtpRequestResultStatus.RateLimited)]
    public async Task RequestPasswordReset_CodeNotIssued_ReturnsRetryAfter(
        OtpIssueStatus issueStatus,
        OtpRequestResultStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Users.Existing = PasswordUser();
        fixture.Otp.IssueResult = new OtpIssueResult(issueStatus, 25);

        var result = await fixture.Service.RequestPasswordResetAsync(new RequestPasswordResetRequest { Email = Email });

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(25, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task RequestPasswordReset_InvalidEmail_ReturnsValidationFailed()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestPasswordResetAsync(new RequestPasswordResetRequest { Email = "abc" });

        Assert.Equal(OtpRequestResultStatus.ValidationFailed, result.Status);
        Assert.Contains("email", result.ValidationErrors!.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Short12")]
    [InlineData("         ")]
    public async Task ConfirmPasswordReset_InvalidNewPassword_ReturnsValidationFailedWithoutUsingAttempt(string newPassword)
    {
        var fixture = new Fixture();
        fixture.Users.Existing = PasswordUser();

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest("123456", newPassword));

        Assert.Equal(PasswordResetResultStatus.ValidationFailed, result.Status);
        Assert.Contains("newPassword", result.ValidationErrors!.Keys);
        Assert.Empty(fixture.Otp.Verified);
    }

    [Fact]
    public async Task ConfirmPasswordReset_NewPasswordTooLong_ReturnsValidationFailed()
    {
        var fixture = new Fixture();
        fixture.Users.Existing = PasswordUser();

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest("123456", new string('a', 129)));

        Assert.Equal(PasswordResetResultStatus.ValidationFailed, result.Status);
        Assert.Contains("newPassword", result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task ConfirmPasswordReset_MalformedCode_ReturnsValidationFailed()
    {
        var fixture = new Fixture();
        fixture.Users.Existing = PasswordUser();

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest("12a456"));

        Assert.Equal(PasswordResetResultStatus.ValidationFailed, result.Status);
        Assert.Contains("code", result.ValidationErrors!.Keys);
        Assert.Empty(fixture.Otp.Verified);
    }

    [Fact]
    public async Task ConfirmPasswordReset_UnknownEmail_ReturnsInvalidOtpWithoutCheckingCode()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest("123456"));

        Assert.Equal(PasswordResetResultStatus.InvalidOtp, result.Status);
        Assert.Empty(fixture.Otp.Verified);
    }

    [Fact]
    public async Task ConfirmPasswordReset_GoogleOnlyAccount_ReturnsInvalidOtpWithoutSettingPassword()
    {
        var fixture = new Fixture();
        fixture.Users.Existing = GoogleOnlyUser();

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest("123456"));

        Assert.Equal(PasswordResetResultStatus.InvalidOtp, result.Status);
        Assert.Empty(fixture.Otp.Verified);
        Assert.Empty(fixture.Users.PasswordUpdates);
    }

    [Fact]
    public async Task ConfirmPasswordReset_WrongCode_ReturnsRemainingAttemptsAndKeepsPassword()
    {
        var fixture = new Fixture();
        fixture.Users.Existing = PasswordUser();
        fixture.Otp.VerifyResult = OtpVerifyResult.Invalid(2);

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest("123456"));

        Assert.Equal(PasswordResetResultStatus.InvalidOtp, result.Status);
        Assert.Equal(2, result.RemainingAttempts);
        Assert.Empty(fixture.Users.PasswordUpdates);
    }

    [Theory]
    [InlineData(OtpVerifyStatus.Expired, PasswordResetResultStatus.OtpExpired)]
    [InlineData(OtpVerifyStatus.AttemptsExceeded, PasswordResetResultStatus.OtpAttemptsExceeded)]
    public async Task ConfirmPasswordReset_OtpRejected_MapsStatusAndKeepsPassword(
        OtpVerifyStatus otpStatus,
        PasswordResetResultStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Users.Existing = PasswordUser();
        fixture.Otp.VerifyResult = new OtpVerifyResult(otpStatus);

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest("123456"));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Empty(fixture.Users.PasswordUpdates);
    }

    [Fact]
    public async Task ConfirmPasswordReset_CorrectCode_StoresHashOfNewPassword()
    {
        var fixture = new Fixture();
        var user = PasswordUser();
        fixture.Users.Existing = user;

        var result = await fixture.Service.ConfirmPasswordResetAsync(ConfirmRequest(" 123456 ", email: " User@Example.com "));

        Assert.Equal(PasswordResetResultStatus.Success, result.Status);
        Assert.Equal((Email, OtpPurpose.PasswordReset, "123456"), Assert.Single(fixture.Otp.Verified));

        var update = Assert.Single(fixture.Users.PasswordUpdates);
        Assert.Same(user, update.User);
        Assert.Equal(FakePasswordHasher.HashOf(NewPassword), update.PasswordHash);
    }

    private static User PasswordUser() => new()
    {
        FullName = "Nguyen Van A",
        Email = Email,
        PasswordHash = FakePasswordHasher.HashOf(OldPassword),
        Role = UserRole.User
    };

    private static User GoogleOnlyUser() => new()
    {
        FullName = "Nguyen Van A",
        Email = Email,
        PasswordHash = null,
        Role = UserRole.User
    };

    private static ConfirmPasswordResetRequest ConfirmRequest(
        string code,
        string newPassword = NewPassword,
        string email = Email) => new()
    {
        Email = email,
        Code = code,
        NewPassword = newPassword
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            Service = new AuthService(
                Users,
                new FakeExternalLoginRepository(),
                new FakePendingRegistrationRepository(),
                Otp,
                new FakePasswordHasher(),
                new FakeAccessTokenService(),
                new FakeGoogleValidator(),
                new FixedTimeProvider(new DateTimeOffset(Now)));
        }

        public FakeUserRepository Users { get; } = new();
        public FakeEmailOtpService Otp { get; } = new();
        public AuthService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeEmailOtpService : IEmailOtpService
    {
        public OtpIssueResult IssueResult { get; set; } = OtpIssueResult.Sent();
        public OtpVerifyResult VerifyResult { get; set; } = OtpVerifyResult.Success();
        public List<(string Email, OtpPurpose Purpose)> Issued { get; } = [];
        public List<(string Email, OtpPurpose Purpose, string Code)> Verified { get; } = [];

        public Task<OtpIssueResult> IssueAsync(
            string email,
            OtpPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            Issued.Add((email, purpose));
            return Task.FromResult(IssueResult);
        }

        public Task<OtpVerifyResult> VerifyAsync(
            string email,
            OtpPurpose purpose,
            string code,
            CancellationToken cancellationToken = default)
        {
            Verified.Add((email, purpose, code));
            return Task.FromResult(VerifyResult);
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public User? Existing { get; set; }
        public List<(User User, string PasswordHash)> PasswordUpdates { get; } = [];

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing?.Email == email);

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing?.Email == email ? Existing : null);

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task UpdatePasswordHashAsync(
            User user,
            string passwordHash,
            CancellationToken cancellationToken = default)
        {
            PasswordUpdates.Add((user, passwordHash));
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakePendingRegistrationRepository : IPendingRegistrationRepository
    {
        public Task<PendingRegistration?> GetByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PendingRegistration?>(null);

        public Task UpsertAsync(
            string email,
            string fullName,
            string passwordHash,
            DateTime expiresAt,
            DateTime now,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryCompleteRegistrationAsync(User user, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task DeleteByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeExternalLoginRepository : IExternalLoginRepository
    {
        public Task<User?> GetUserByExternalLoginAsync(
            string provider,
            string providerSubject,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<bool> TryCreateUserWithExternalLoginAsync(
            User user,
            UserExternalLogin externalLogin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakePasswordHasher : IPasswordHashService
    {
        public static string HashOf(string password) => $"hashed:{password}";

        public string HashPassword(User user, string password) => HashOf(password);

        public PasswordHashVerificationResult VerifyHashedPassword(
            User user,
            string hashedPassword,
            string providedPassword) =>
            hashedPassword == HashOf(providedPassword)
                ? PasswordHashVerificationResult.Success
                : PasswordHashVerificationResult.Failed;
    }

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public AccessTokenResult CreateAccessToken(User user) =>
            new("token", new DateTimeOffset(Now).AddHours(1));

        public AccessTokenResult CreateDemoAccessToken(Guid sessionId) =>
            new("demo-token", new DateTimeOffset(Now).AddHours(1));
    }

    private sealed class FakeGoogleValidator : IGoogleIdentityTokenValidator
    {
        public Task<VerifiedExternalIdentity?> ValidateAsync(
            string idToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<VerifiedExternalIdentity?>(null);
    }
}
