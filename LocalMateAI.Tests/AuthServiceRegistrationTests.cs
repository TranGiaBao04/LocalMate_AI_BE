using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AuthServiceRegistrationTests
{
    private const string Email = "user@example.com";
    private const string Password = "Secret123";

    private static readonly DateTime Now = new(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Register_Valid_IssuesCodeAndStoresPendingWithHashedPassword()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RegisterAsync(new RegisterRequest
        {
            FullName = "  Nguyen Van A  ",
            Email = "  User@Example.COM ",
            Password = Password
        });

        Assert.Equal(RegisterResultStatus.Success, result.Status);
        Assert.Equal(new OtpDispatchResponse(Email, 600, 60), result.Response);

        Assert.Equal((Email, OtpPurpose.Registration), Assert.Single(fixture.Otp.Issued));

        var pending = Assert.Single(fixture.Pending.Upserts);
        Assert.Equal(Email, pending.Email);
        Assert.Equal("Nguyen Van A", pending.FullName);
        Assert.Equal(FakePasswordHasher.HashOf(Password), pending.PasswordHash);
        Assert.Equal(Now.AddHours(24), pending.ExpiresAt);
    }

    [Fact]
    public async Task Register_EmailAlreadyRegistered_ReturnsDuplicateWithoutIssuingCode()
    {
        var fixture = new Fixture();
        fixture.Users.ExistingEmails.Add(Email);

        var result = await fixture.Service.RegisterAsync(ValidRegisterRequest());

        Assert.Equal(RegisterResultStatus.DuplicateEmail, result.Status);
        Assert.Empty(fixture.Otp.Issued);
        Assert.Empty(fixture.Pending.Upserts);
    }

    [Fact]
    public async Task Register_InvalidInput_ReturnsValidationFailedWithoutIssuingCode()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RegisterAsync(new RegisterRequest
        {
            FullName = "",
            Email = "not-an-email",
            Password = "short"
        });

        Assert.Equal(RegisterResultStatus.ValidationFailed, result.Status);
        Assert.Contains("fullName", result.ValidationErrors!.Keys);
        Assert.Contains("email", result.ValidationErrors.Keys);
        Assert.Contains("password", result.ValidationErrors.Keys);
        Assert.Empty(fixture.Otp.Issued);
    }

    [Theory]
    [InlineData(OtpIssueStatus.Cooldown, RegisterResultStatus.Cooldown)]
    [InlineData(OtpIssueStatus.RateLimited, RegisterResultStatus.RateLimited)]
    public async Task Register_CodeNotIssued_ReturnsRetryAfterWithoutOverwritingPending(
        OtpIssueStatus issueStatus,
        RegisterResultStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Otp.IssueResult = new OtpIssueResult(issueStatus, 42);

        var result = await fixture.Service.RegisterAsync(ValidRegisterRequest());

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(42, result.RetryAfterSeconds);
        Assert.Empty(fixture.Pending.Upserts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public async Task VerifyRegistration_MalformedCode_ReturnsValidationFailedWithoutCheckingOtp(string code)
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending();

        var result = await fixture.Service.VerifyRegistrationAsync(VerifyRequest(code));

        Assert.Equal(VerifyRegistrationResultStatus.ValidationFailed, result.Status);
        Assert.Contains("code", result.ValidationErrors!.Keys);
        Assert.Empty(fixture.Otp.Verified);
    }

    [Fact]
    public async Task VerifyRegistration_NoPendingRegistration_ReturnsInvalidOtpWithoutCheckingOtp()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.VerifyRegistrationAsync(VerifyRequest("123456"));

        Assert.Equal(VerifyRegistrationResultStatus.InvalidOtp, result.Status);
        Assert.Empty(fixture.Otp.Verified);
    }

    [Fact]
    public async Task VerifyRegistration_PendingRegistrationExpired_ReturnsInvalidOtp()
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending(expiresAt: Now);

        var result = await fixture.Service.VerifyRegistrationAsync(VerifyRequest("123456"));

        Assert.Equal(VerifyRegistrationResultStatus.InvalidOtp, result.Status);
        Assert.Empty(fixture.Otp.Verified);
    }

    [Fact]
    public async Task VerifyRegistration_WrongCode_ReturnsRemainingAttemptsWithoutCreatingUser()
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending();
        fixture.Otp.VerifyResult = OtpVerifyResult.Invalid(3);

        var result = await fixture.Service.VerifyRegistrationAsync(VerifyRequest("123456"));

        Assert.Equal(VerifyRegistrationResultStatus.InvalidOtp, result.Status);
        Assert.Equal(3, result.RemainingAttempts);
        Assert.Empty(fixture.Pending.CompletedUsers);
    }

    [Theory]
    [InlineData(OtpVerifyStatus.Expired, VerifyRegistrationResultStatus.OtpExpired)]
    [InlineData(OtpVerifyStatus.AttemptsExceeded, VerifyRegistrationResultStatus.OtpAttemptsExceeded)]
    public async Task VerifyRegistration_OtpRejected_MapsStatusWithoutCreatingUser(
        OtpVerifyStatus otpStatus,
        VerifyRegistrationResultStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending();
        fixture.Otp.VerifyResult = new OtpVerifyResult(otpStatus);

        var result = await fixture.Service.VerifyRegistrationAsync(VerifyRequest("123456"));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Empty(fixture.Pending.CompletedUsers);
    }

    [Fact]
    public async Task VerifyRegistration_CorrectCode_CreatesUserFromPendingRegistration()
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending();

        var result = await fixture.Service.VerifyRegistrationAsync(VerifyRequest(" 123456 ", " User@Example.com "));

        Assert.Equal(VerifyRegistrationResultStatus.Success, result.Status);
        Assert.Equal((Email, OtpPurpose.Registration, "123456"), Assert.Single(fixture.Otp.Verified));

        var user = Assert.Single(fixture.Pending.CompletedUsers);
        Assert.Equal("Nguyen Van A", user.FullName);
        Assert.Equal(Email, user.Email);
        Assert.Equal(FakePasswordHasher.HashOf(Password), user.PasswordHash);
        Assert.Equal(UserRole.User, user.Role);

        Assert.Equal(user.Id, result.Response!.Id);
        Assert.Equal(Email, result.Response.Email);
        Assert.Equal("User", result.Response.Role);
    }

    [Fact]
    public async Task VerifyRegistration_EmailTakenWhileWaiting_ReturnsDuplicateEmail()
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending();
        fixture.Pending.CompleteSucceeds = false;

        var result = await fixture.Service.VerifyRegistrationAsync(VerifyRequest("123456"));

        Assert.Equal(VerifyRegistrationResultStatus.DuplicateEmail, result.Status);
    }

    [Fact]
    public async Task ResendRegistrationOtp_NoPendingRegistration_ReturnsAcceptedWithoutSending()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ResendRegistrationOtpAsync(new ResendRegistrationOtpRequest { Email = Email });

        Assert.Equal(OtpRequestResultStatus.Accepted, result.Status);
        Assert.Equal(new OtpDispatchResponse(Email, 600, 60), result.Response);
        Assert.Empty(fixture.Otp.Issued);
    }

    [Fact]
    public async Task ResendRegistrationOtp_PendingRegistrationExpired_ReturnsAcceptedWithoutSending()
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending(expiresAt: Now.AddMinutes(-1));

        var result = await fixture.Service.ResendRegistrationOtpAsync(new ResendRegistrationOtpRequest { Email = Email });

        Assert.Equal(OtpRequestResultStatus.Accepted, result.Status);
        Assert.Empty(fixture.Otp.Issued);
    }

    [Fact]
    public async Task ResendRegistrationOtp_PendingRegistration_IssuesNewCode()
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending();

        var result = await fixture.Service.ResendRegistrationOtpAsync(new ResendRegistrationOtpRequest { Email = Email });

        Assert.Equal(OtpRequestResultStatus.Accepted, result.Status);
        Assert.Equal((Email, OtpPurpose.Registration), Assert.Single(fixture.Otp.Issued));
    }

    [Theory]
    [InlineData(OtpIssueStatus.Cooldown, OtpRequestResultStatus.Cooldown)]
    [InlineData(OtpIssueStatus.RateLimited, OtpRequestResultStatus.RateLimited)]
    public async Task ResendRegistrationOtp_CodeNotIssued_ReturnsRetryAfter(
        OtpIssueStatus issueStatus,
        OtpRequestResultStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Pending.Existing = Pending();
        fixture.Otp.IssueResult = new OtpIssueResult(issueStatus, 30);

        var result = await fixture.Service.ResendRegistrationOtpAsync(new ResendRegistrationOtpRequest { Email = Email });

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(30, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task ResendRegistrationOtp_InvalidEmail_ReturnsValidationFailed()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ResendRegistrationOtpAsync(new ResendRegistrationOtpRequest { Email = "abc" });

        Assert.Equal(OtpRequestResultStatus.ValidationFailed, result.Status);
        Assert.Contains("email", result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task GoogleSignIn_NewUser_DeletesPendingRegistrationOfSameEmail()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.GoogleSignInAsync(new GoogleSignInRequest("google-id-token"));

        Assert.Equal(GoogleSignInResultStatus.Success, result.Status);
        Assert.Equal(Email, Assert.Single(fixture.Pending.DeletedEmails));
    }

    [Fact]
    public async Task GoogleSignIn_AlreadyLinkedUser_DoesNotTouchPendingRegistrations()
    {
        var fixture = new Fixture();
        fixture.ExternalLogins.LinkedUser = new User { FullName = "Nguyen Van A", Email = Email };

        var result = await fixture.Service.GoogleSignInAsync(new GoogleSignInRequest("google-id-token"));

        Assert.Equal(GoogleSignInResultStatus.Success, result.Status);
        Assert.Empty(fixture.Pending.DeletedEmails);
    }

    private static RegisterRequest ValidRegisterRequest() => new()
    {
        FullName = "Nguyen Van A",
        Email = Email,
        Password = Password
    };

    private static VerifyRegistrationRequest VerifyRequest(string code, string email = Email) => new()
    {
        Email = email,
        Code = code
    };

    private static PendingRegistration Pending(DateTime? expiresAt = null) => new()
    {
        Email = Email,
        FullName = "Nguyen Van A",
        PasswordHash = FakePasswordHasher.HashOf(Password),
        ExpiresAt = expiresAt ?? Now.AddHours(23)
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            Service = new AuthService(
                Users,
                ExternalLogins,
                Pending,
                Otp,
                new FakePasswordHasher(),
                new FakeAccessTokenService(),
                new FakeGoogleValidator(),
                new FixedTimeProvider(new DateTimeOffset(Now)));
        }

        public FakeUserRepository Users { get; } = new();
        public FakeExternalLoginRepository ExternalLogins { get; } = new();
        public FakePendingRegistrationRepository Pending { get; } = new();
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

    private sealed class FakePendingRegistrationRepository : IPendingRegistrationRepository
    {
        public PendingRegistration? Existing { get; set; }
        public bool CompleteSucceeds { get; set; } = true;
        public List<PendingRegistration> Upserts { get; } = [];
        public List<User> CompletedUsers { get; } = [];
        public List<string> DeletedEmails { get; } = [];

        public Task<PendingRegistration?> GetByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing?.Email == email ? Existing : null);

        public Task UpsertAsync(
            string email,
            string fullName,
            string passwordHash,
            DateTime expiresAt,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            Upserts.Add(new PendingRegistration
            {
                Email = email,
                FullName = fullName,
                PasswordHash = passwordHash,
                ExpiresAt = expiresAt
            });
            return Task.CompletedTask;
        }

        public Task<bool> TryCompleteRegistrationAsync(User user, CancellationToken cancellationToken = default)
        {
            if (CompleteSucceeds)
            {
                CompletedUsers.Add(user);
            }

            return Task.FromResult(CompleteSucceeds);
        }

        public Task DeleteByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            DeletedEmails.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public HashSet<string> ExistingEmails { get; } = [];

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingEmails.Contains(email));

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task UpdatePasswordHashAsync(
            User user,
            string passwordHash,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeExternalLoginRepository : IExternalLoginRepository
    {
        public User? LinkedUser { get; set; }

        public Task<User?> GetUserByExternalLoginAsync(
            string provider,
            string providerSubject,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LinkedUser);

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
            Task.FromResult<VerifiedExternalIdentity?>(
                new VerifiedExternalIdentity("google-subject", Email, "Nguyen Van A"));
    }
}
