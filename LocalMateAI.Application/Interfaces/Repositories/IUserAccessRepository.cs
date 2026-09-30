using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces.Repositories;

// Repository riêng, không thêm vào IUserRepository (25 file test đang fake interface đó).
public interface IUserAccessRepository
{
    Task<UserAccessReadModel?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}
