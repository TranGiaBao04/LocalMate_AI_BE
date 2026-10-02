using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PendingRegistrationRepository(AppDbContext dbContext) : IPendingRegistrationRepository
{
    private const string UserEmailConstraintName = "UX_Users_Email";

    public Task<PendingRegistration?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default) =>
        dbContext.PendingRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(pending => pending.Email == email, cancellationToken);

    public async Task UpsertAsync(
        string email,
        string fullName,
        string passwordHash,
        DateTime expiresAt,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        // Một câu INSERT ... ON CONFLICT để 2 request đăng ký cùng email không đụng unique index.
        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "PendingRegistrations" ("Id", "Email", "FullName", "PasswordHash", "ExpiresAt", "CreatedAt", "UpdatedAt")
            VALUES ({Guid.NewGuid()}, {email}, {fullName}, {passwordHash}, {expiresAt}, {now}, {now})
            ON CONFLICT ("Email") DO UPDATE SET
                "FullName" = EXCLUDED."FullName",
                "PasswordHash" = EXCLUDED."PasswordHash",
                "ExpiresAt" = EXCLUDED."ExpiresAt",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            cancellationToken);
    }

    public async Task<bool> TryCompleteRegistrationAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            dbContext.Entry(user).State = EntityState.Detached;
            await transaction.RollbackAsync(cancellationToken);

            // Email đã có tài khoản: bản đăng ký tạm không còn dùng được nữa.
            await DeleteByEmailAsync(user.Email, cancellationToken);
            return false;
        }

        await dbContext.PendingRegistrations
            .Where(pending => pending.Email == user.Email)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task DeleteByEmailAsync(
        string email,
        CancellationToken cancellationToken = default) =>
        dbContext.PendingRegistrations
            .Where(pending => pending.Email == email)
            .ExecuteDeleteAsync(cancellationToken);

    private static bool IsDuplicateEmailViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UserEmailConstraintName
        };
}
