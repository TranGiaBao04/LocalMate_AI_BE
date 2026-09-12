using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class ExternalLoginRepository(AppDbContext dbContext) : IExternalLoginRepository
{
    private const string ExternalIdentityConstraintName = "PK_USER_EXTERNAL_LOGINS";
    private const string UserProviderConstraintName =
        "UX_USER_EXTERNAL_LOGINS_UserId_Provider";
    private const string UserEmailConstraintName = "UX_USERS_Email";

    public Task<User?> GetUserByExternalLoginAsync(
        string provider,
        string providerSubject,
        CancellationToken cancellationToken = default) =>
        dbContext.UserExternalLogins
            .AsNoTracking()
            .Where(externalLogin =>
                externalLogin.Provider == provider
                && externalLogin.ProviderSubject == providerSubject)
            .Join(
                dbContext.Users.AsNoTracking(),
                externalLogin => externalLogin.UserId,
                user => user.Id,
                (_, user) => user)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryCreateUserWithExternalLoginAsync(
        User user,
        UserExternalLogin externalLogin,
        CancellationToken cancellationToken = default)
    {
        dbContext.Users.Add(user);
        dbContext.UserExternalLogins.Add(externalLogin);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsKnownIdentityConflict(exception))
        {
            dbContext.Entry(externalLogin).State = EntityState.Detached;
            dbContext.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsKnownIdentityConflict(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } postgresException)
        {
            return false;
        }

        return postgresException.ConstraintName is ExternalIdentityConstraintName
            or UserProviderConstraintName
            or UserEmailConstraintName;
    }
}
