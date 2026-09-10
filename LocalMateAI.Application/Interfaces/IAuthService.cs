using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces;

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);
}
