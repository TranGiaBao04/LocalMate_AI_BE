using LocalMateAI.Application.DTOs.Users;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IUserService
{
    Task<UserProfileResponse?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
