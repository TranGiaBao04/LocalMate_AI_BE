using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ISystemRoleProvider
{
    Task<SystemRoleReference> GetUserRoleAsync(CancellationToken cancellationToken = default);

    Task<SystemRoleReference> GetAdminRoleAsync(CancellationToken cancellationToken = default);
}
