using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class UserRepository(AppDbContext dbContext) : IUserRepository
{
    private const string UserEmailConstraintName = "UX_USERS_Email";

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.Users.AsNoTracking().AnyAsync(user => user.Email == email, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .Include(user => user.PreferenceTags)
            .ThenInclude(preferenceTag => preferenceTag.Tag)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task<User?> GetByIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(user => user.PreferenceTags)
            .ThenInclude(preferenceTag => preferenceTag.Tag)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default)
    {
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            dbContext.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    public async Task UpdatePasswordHashAsync(
        User user,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        user.PasswordHash = passwordHash;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveProfileChangesAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        dbContext.Entry(user)
            .Property(entry => entry.UpdatedAt)
            .IsModified = true;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(
            postgresException.ConstraintName,
            UserEmailConstraintName,
            StringComparison.Ordinal);
}
