using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<VerifyRegistrationResult> VerifyRegistrationAsync(
        VerifyRegistrationRequest request,
        CancellationToken cancellationToken = default);

    Task<OtpRequestResult> ResendRegistrationOtpAsync(
        ResendRegistrationOtpRequest request,
        CancellationToken cancellationToken = default);

    Task<OtpRequestResult> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken = default);

    Task<PasswordResetResult> ConfirmPasswordResetAsync(
        ConfirmPasswordResetRequest request,
        CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<GoogleSignInResult> GoogleSignInAsync(
        GoogleSignInRequest request,
        CancellationToken cancellationToken = default);

    DemoSessionResponse CreateDemoSession();
}
