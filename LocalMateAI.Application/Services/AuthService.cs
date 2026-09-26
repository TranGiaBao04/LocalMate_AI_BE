using System.ComponentModel.DataAnnotations;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class AuthService(
    IUserRepository userRepository,
    IExternalLoginRepository externalLoginRepository,
    IPendingRegistrationRepository pendingRegistrationRepository,
    IEmailOtpService emailOtpService,
    IPasswordHashService passwordHashService,
    IAccessTokenService accessTokenService,
    IGoogleIdentityTokenValidator googleIdentityTokenValidator,
    TimeProvider timeProvider) : IAuthService
{
    private const string GoogleProvider = "Google";
    private const int MaximumFullNameLength = 200;
    private const int MaximumEmailLength = 254;
    private const int MaximumProviderSubjectLength = 255;
    private const int MaximumGoogleIdTokenLength = 16_384;
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 128;
    private const int OtpCodeLength = 6;

    // Bản đăng ký chưa nhập OTP được giữ 24 giờ; quá hạn phải đăng ký lại.
    private static readonly TimeSpan PendingRegistrationLifetime = TimeSpan.FromHours(24);

    private static readonly EmailAddressAttribute EmailValidator = new();

    public async Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var validationErrors = Validate(fullName, email, password);
        if (validationErrors.Count > 0)
        {
            return RegisterResult.ValidationFailed(validationErrors);
        }

        var emailExists = await userRepository.EmailExistsAsync(email, cancellationToken);
        if (emailExists)
        {
            return RegisterResult.EmailAlreadyExists();
        }

        var passwordHash = passwordHashService.HashPassword(
            new User { FullName = fullName, Email = email },
            password);

        // Phát mã trước: đang trong thời gian chờ thì không ghi đè tên/mật khẩu của bản đăng ký tạm.
        var issueResult = await emailOtpService.IssueAsync(email, OtpPurpose.Registration, cancellationToken);
        switch (issueResult.Status)
        {
            case OtpIssueStatus.Cooldown:
                return RegisterResult.Cooldown(issueResult.RetryAfterSeconds);
            case OtpIssueStatus.RateLimited:
                return RegisterResult.RateLimited(issueResult.RetryAfterSeconds);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await pendingRegistrationRepository.UpsertAsync(
            email,
            fullName,
            passwordHash,
            now + PendingRegistrationLifetime,
            now,
            cancellationToken);

        return RegisterResult.Succeeded(CreateOtpDispatchResponse(email));
    }

    public async Task<VerifyRegistrationResult> VerifyRegistrationAsync(
        VerifyRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var code = request.Code?.Trim() ?? string.Empty;

        var validationErrors = ValidateOtpVerification(email, code);
        if (validationErrors.Count > 0)
        {
            return VerifyRegistrationResult.ValidationFailed(validationErrors);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var pending = await pendingRegistrationRepository.GetByEmailAsync(email, cancellationToken);
        if (pending is null || pending.ExpiresAt <= now)
        {
            return VerifyRegistrationResult.InvalidOtp();
        }

        var otpResult = await emailOtpService.VerifyAsync(
            email,
            OtpPurpose.Registration,
            code,
            cancellationToken);

        switch (otpResult.Status)
        {
            case OtpVerifyStatus.Invalid:
                return VerifyRegistrationResult.InvalidOtp(otpResult.RemainingAttempts);
            case OtpVerifyStatus.Expired:
                return VerifyRegistrationResult.OtpExpired();
            case OtpVerifyStatus.AttemptsExceeded:
                return VerifyRegistrationResult.OtpAttemptsExceeded();
        }

        var user = new User
        {
            FullName = pending.FullName,
            Email = pending.Email,
            PasswordHash = pending.PasswordHash,
            Role = UserRole.User
        };

        var created = await pendingRegistrationRepository.TryCompleteRegistrationAsync(user, cancellationToken);
        if (!created)
        {
            // Email vừa được dùng để tạo tài khoản khác (vd. đăng nhập Google) trong lúc chờ OTP.
            return VerifyRegistrationResult.EmailAlreadyExists();
        }

        var response = new RegisterResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.Role.ToString(),
            user.CreatedAt);

        return VerifyRegistrationResult.Succeeded(response);
    }

    public async Task<OtpRequestResult> ResendRegistrationOtpAsync(
        ResendRegistrationOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        var validationErrors = ValidateEmailOnly(email);
        if (validationErrors.Count > 0)
        {
            return OtpRequestResult.ValidationFailed(validationErrors);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var pending = await pendingRegistrationRepository.GetByEmailAsync(email, cancellationToken);

        // Không có bản đăng ký tạm vẫn trả như đã gửi, để không lộ email nào đang chờ xác thực.
        if (pending is null || pending.ExpiresAt <= now)
        {
            return OtpRequestResult.Accepted(CreateOtpDispatchResponse(email));
        }

        var issueResult = await emailOtpService.IssueAsync(email, OtpPurpose.Registration, cancellationToken);

        return issueResult.Status switch
        {
            OtpIssueStatus.Cooldown => OtpRequestResult.Cooldown(issueResult.RetryAfterSeconds),
            OtpIssueStatus.RateLimited => OtpRequestResult.RateLimited(issueResult.RetryAfterSeconds),
            _ => OtpRequestResult.Accepted(CreateOtpDispatchResponse(email))
        };
    }

    public async Task<OtpRequestResult> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        var validationErrors = ValidateEmailOnly(email);
        if (validationErrors.Count > 0)
        {
            return OtpRequestResult.ValidationFailed(validationErrors);
        }

        var user = await userRepository.GetByEmailAsync(email, cancellationToken);

        // Email chưa đăng ký vẫn trả như đã gửi, để không lộ email nào có tài khoản.
        if (user is null)
        {
            return OtpRequestResult.Accepted(CreateOtpDispatchResponse(email));
        }

        // Tài khoản chỉ đăng nhập bằng Google không có mật khẩu để đặt lại (và không được thêm mật khẩu qua đường này).
        if (user.PasswordHash is null)
        {
            return OtpRequestResult.GoogleAccountWithoutPassword();
        }

        var issueResult = await emailOtpService.IssueAsync(email, OtpPurpose.PasswordReset, cancellationToken);

        return issueResult.Status switch
        {
            OtpIssueStatus.Cooldown => OtpRequestResult.Cooldown(issueResult.RetryAfterSeconds),
            OtpIssueStatus.RateLimited => OtpRequestResult.RateLimited(issueResult.RetryAfterSeconds),
            _ => OtpRequestResult.Accepted(CreateOtpDispatchResponse(email))
        };
    }

    public async Task<PasswordResetResult> ConfirmPasswordResetAsync(
        ConfirmPasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var code = request.Code?.Trim() ?? string.Empty;
        var newPassword = request.NewPassword ?? string.Empty;

        // Kiểm tra mật khẩu mới trước khi đụng tới mã, để nhập sai mật khẩu không tốn lượt nhập mã.
        var validationErrors = ValidateOtpVerification(email, code);
        var passwordError = GetPasswordError(newPassword);
        if (passwordError is not null)
        {
            validationErrors["newPassword"] = [passwordError];
        }

        if (validationErrors.Count > 0)
        {
            return PasswordResetResult.ValidationFailed(validationErrors);
        }

        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null || user.PasswordHash is null)
        {
            // Không có tài khoản mật khẩu thì cũng không bao giờ có mã: trả như mã sai.
            return PasswordResetResult.InvalidOtp();
        }

        var otpResult = await emailOtpService.VerifyAsync(
            email,
            OtpPurpose.PasswordReset,
            code,
            cancellationToken);

        switch (otpResult.Status)
        {
            case OtpVerifyStatus.Invalid:
                return PasswordResetResult.InvalidOtp(otpResult.RemainingAttempts);
            case OtpVerifyStatus.Expired:
                return PasswordResetResult.OtpExpired();
            case OtpVerifyStatus.AttemptsExceeded:
                return PasswordResetResult.OtpAttemptsExceeded();
        }

        var newPasswordHash = passwordHashService.HashPassword(user, newPassword);
        await userRepository.UpdatePasswordHashAsync(user, newPasswordHash, cancellationToken);

        return PasswordResetResult.Succeeded();
    }

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var validationErrors = ValidateLogin(email, password);
        if (validationErrors.Count > 0)
        {
            return LoginResult.ValidationFailed(validationErrors);
        }

        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null || user.PasswordHash is null)
        {
            _ = passwordHashService.HashPassword(new User(), password);
            return LoginResult.InvalidCredentials();
        }

        var verificationResult = passwordHashService.VerifyHashedPassword(
            user,
            user.PasswordHash,
            password);

        if (verificationResult == PasswordHashVerificationResult.Failed)
        {
            return LoginResult.InvalidCredentials();
        }

        if (verificationResult == PasswordHashVerificationResult.SuccessRehashNeeded)
        {
            var updatedPasswordHash = passwordHashService.HashPassword(user, password);
            await userRepository.UpdatePasswordHashAsync(
                user,
                updatedPasswordHash,
                cancellationToken);
        }

        return LoginResult.Succeeded(CreateLoginResponse(user));
    }

    public async Task<GoogleSignInResult> GoogleSignInAsync(
        GoogleSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var idToken = request.IdToken?.Trim() ?? string.Empty;
        var validationErrors = ValidateGoogleSignIn(idToken);
        if (validationErrors.Count > 0)
        {
            return GoogleSignInResult.ValidationFailed(validationErrors);
        }

        var identity = await googleIdentityTokenValidator.ValidateAsync(
            idToken,
            cancellationToken);

        if (identity is null)
        {
            return GoogleSignInResult.InvalidGoogleToken();
        }

        var providerSubject = identity.ProviderSubject.Trim();
        if (string.IsNullOrWhiteSpace(providerSubject)
            || providerSubject.Length > MaximumProviderSubjectLength)
        {
            return GoogleSignInResult.InvalidGoogleToken();
        }

        var linkedUser = await externalLoginRepository.GetUserByExternalLoginAsync(
            GoogleProvider,
            providerSubject,
            cancellationToken);

        if (linkedUser is not null)
        {
            return GoogleSignInResult.Succeeded(CreateLoginResponse(linkedUser));
        }

        var email = identity.Email.Trim().ToLowerInvariant();
        var fullName = identity.FullName.Trim();
        if (!IsValidExternalEmail(email)
            || string.IsNullOrWhiteSpace(fullName)
            || fullName.Length > MaximumFullNameLength)
        {
            return GoogleSignInResult.InvalidGoogleToken();
        }

        var emailOwner = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (emailOwner is not null)
        {
            return GoogleSignInResult.AccountLinkRequired();
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = null,
            Role = UserRole.User
        };

        var externalLogin = new UserExternalLogin
        {
            UserId = user.Id,
            Provider = GoogleProvider,
            ProviderSubject = providerSubject
        };

        var created = await externalLoginRepository.TryCreateUserWithExternalLoginAsync(
            user,
            externalLogin,
            cancellationToken);

        if (created)
        {
            // Email đã được Google xác nhận: bản đăng ký bằng mật khẩu đang chờ OTP (nếu có) không còn cần nữa.
            await pendingRegistrationRepository.DeleteByEmailAsync(email, cancellationToken);
            return GoogleSignInResult.Succeeded(CreateLoginResponse(user));
        }

        linkedUser = await externalLoginRepository.GetUserByExternalLoginAsync(
            GoogleProvider,
            providerSubject,
            cancellationToken);

        if (linkedUser is not null)
        {
            return GoogleSignInResult.Succeeded(CreateLoginResponse(linkedUser));
        }

        var emailWasClaimed = await userRepository.EmailExistsAsync(email, cancellationToken);
        return emailWasClaimed
            ? GoogleSignInResult.AccountLinkRequired()
            : GoogleSignInResult.AccountConflict();
    }

    public DemoSessionResponse CreateDemoSession()
    {
        var sessionId = Guid.NewGuid();
        var accessToken = accessTokenService.CreateDemoAccessToken(sessionId);

        return new DemoSessionResponse(
            accessToken.AccessToken,
            "Bearer",
            accessToken.ExpiresAt,
            "demo");
    }

    private static Dictionary<string, string[]> Validate(
        string fullName,
        string email,
        string password)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors["fullName"] = ["Full name is required."];
        }
        else if (fullName.Length > MaximumFullNameLength)
        {
            errors["fullName"] = [$"Full name must not exceed {MaximumFullNameLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (email.Length > MaximumEmailLength)
        {
            errors["email"] = [$"Email must not exceed {MaximumEmailLength} characters."];
        }
        else if (!EmailValidator.IsValid(email))
        {
            errors["email"] = ["Email format is invalid."];
        }

        var passwordError = GetPasswordError(password);
        if (passwordError is not null)
        {
            errors["password"] = [passwordError];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateLogin(string email, string password)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (email.Length > MaximumEmailLength)
        {
            errors["email"] = [$"Email must not exceed {MaximumEmailLength} characters."];
        }
        else if (!EmailValidator.IsValid(email))
        {
            errors["email"] = ["Email format is invalid."];
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Password is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateGoogleSignIn(string idToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(idToken))
        {
            errors["idToken"] = ["Google ID token is required."];
        }
        else if (idToken.Length > MaximumGoogleIdTokenLength)
        {
            errors["idToken"] =
                [$"Google ID token must not exceed {MaximumGoogleIdTokenLength} characters."];
        }

        return errors;
    }

    private static string? GetPasswordError(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Password is required.";
        }

        if (password.Length < MinimumPasswordLength)
        {
            return $"Password must contain at least {MinimumPasswordLength} characters.";
        }

        if (password.Length > MaximumPasswordLength)
        {
            return $"Password must not exceed {MaximumPasswordLength} characters.";
        }

        return string.IsNullOrWhiteSpace(password)
            ? "Password must contain at least one non-whitespace character."
            : null;
    }

    private static Dictionary<string, string[]> ValidateEmailOnly(string email)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (email.Length > MaximumEmailLength)
        {
            errors["email"] = [$"Email must not exceed {MaximumEmailLength} characters."];
        }
        else if (!EmailValidator.IsValid(email))
        {
            errors["email"] = ["Email format is invalid."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateOtpVerification(string email, string code)
    {
        var errors = ValidateEmailOnly(email);

        if (string.IsNullOrEmpty(code))
        {
            errors["code"] = ["Verification code is required."];
        }
        else if (code.Length != OtpCodeLength || !code.All(char.IsAsciiDigit))
        {
            errors["code"] = [$"Verification code must be exactly {OtpCodeLength} digits."];
        }

        return errors;
    }

    private static OtpDispatchResponse CreateOtpDispatchResponse(string email) =>
        new(
            email,
            (int)EmailOtpService.CodeLifetime.TotalSeconds,
            (int)EmailOtpService.ResendCooldown.TotalSeconds);

    private static bool IsValidExternalEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= MaximumEmailLength
        && EmailValidator.IsValid(email);

    private LoginResponse CreateLoginResponse(User user)
    {
        var accessToken = accessTokenService.CreateAccessToken(user);
        return new LoginResponse(
            accessToken.AccessToken,
            "Bearer",
            accessToken.ExpiresAt);
    }
}
