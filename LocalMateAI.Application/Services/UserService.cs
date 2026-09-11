using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class UserService(IUserRepository userRepository) : IUserService
{
    public async Task<UserProfileResponse?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        return user is null
            ? null
            : new UserProfileResponse(
                user.Id,
                user.FullName,
                user.Email,
                user.Role.ToString(),
                user.CreatedAt);
    }
}
