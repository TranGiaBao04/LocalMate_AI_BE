using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    DemoSessionResponse CreateDemoSession();
}
