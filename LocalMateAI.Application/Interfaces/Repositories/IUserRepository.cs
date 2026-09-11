using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default);

    Task UpdatePasswordHashAsync(
        User user,
        string passwordHash,
        CancellationToken cancellationToken = default);
}
