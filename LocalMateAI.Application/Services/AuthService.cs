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
    IPasswordHashService passwordHashService,
    IAccessTokenService accessTokenService,
    IGoogleIdentityTokenValidator googleIdentityTokenValidator) : IAuthService
{
    private const string GoogleProvider = "Google";
    private const int MaximumFullNameLength = 200;
    private const int MaximumEmailLength = 254;
    private const int MaximumProviderSubjectLength = 255;
    private const int MaximumGoogleIdTokenLength = 16_384;
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 128;

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

        var user = new User
        {
            FullName = fullName,
            Email = email,
            Role = UserRole.User
        };

        user.PasswordHash = passwordHashService.HashPassword(user, password);

        var added = await userRepository.TryAddAsync(user, cancellationToken);
        if (!added)
        {
            return RegisterResult.EmailAlreadyExists();
        }

        var response = new RegisterResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.Role.ToString(),
            user.CreatedAt);

        return RegisterResult.Succeeded(response);
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

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (password.Length < MinimumPasswordLength)
        {
            errors["password"] = [$"Password must contain at least {MinimumPasswordLength} characters."];
        }
        else if (password.Length > MaximumPasswordLength)
        {
            errors["password"] = [$"Password must not exceed {MaximumPasswordLength} characters."];
        }
        else if (string.IsNullOrWhiteSpace(password))
        {
            errors["password"] = ["Password must contain at least one non-whitespace character."];
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
