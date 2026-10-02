using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IExternalLoginRepository
{
    Task<User?> GetUserByExternalLoginAsync(
        string provider,
        string providerSubject,
        CancellationToken cancellationToken = default);

    Task<bool> TryCreateUserWithExternalLoginAsync(
        User user,
        UserExternalLogin externalLogin,
        CancellationToken cancellationToken = default);
}
