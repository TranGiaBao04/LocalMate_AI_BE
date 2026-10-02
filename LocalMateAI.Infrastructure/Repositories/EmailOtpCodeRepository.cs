using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class EmailOtpCodeRepository(AppDbContext dbContext) : IEmailOtpCodeRepository
{
    private const string ActiveCodeConstraintName = "UX_EmailOtpCodes_Email_Purpose_Active";

    public async Task<IReadOnlyList<DateTime>> GetCreatedTimesSinceAsync(
        string email,
        OtpPurpose purpose,
        DateTime since,
        CancellationToken cancellationToken = default) =>
        await dbContext.EmailOtpCodes
            .AsNoTracking()
            .Where(otp => otp.Email == email && otp.Purpose == purpose && otp.CreatedAt >= since)
            .OrderBy(otp => otp.CreatedAt)
            .Select(otp => otp.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<EmailOtpCode?> GetActiveAsync(
        string email,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default) =>
        dbContext.EmailOtpCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                otp => otp.Email == email && otp.Purpose == purpose && otp.ConsumedAt == null,
                cancellationToken);

    public async Task<bool> TryReplaceActiveAsync(
        EmailOtpCode otpCode,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.EmailOtpCodes
            .Where(otp => otp.Email == otpCode.Email
                          && otp.Purpose == otpCode.Purpose
                          && otp.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(otp => otp.ConsumedAt, now)
                .SetProperty(otp => otp.UpdatedAt, now), cancellationToken);

        dbContext.EmailOtpCodes.Add(otpCode);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsActiveCodeConflict(exception))
        {
            // Transaction chưa commit sẽ tự rollback khi dispose.
            dbContext.Entry(otpCode).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryIncrementAttemptAsync(
        Guid otpCodeId,
        int maxAttempts,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        await dbContext.EmailOtpCodes
            .Where(otp => otp.Id == otpCodeId
                          && otp.ConsumedAt == null
                          && otp.AttemptCount < maxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(otp => otp.AttemptCount, otp => otp.AttemptCount + 1)
                .SetProperty(otp => otp.UpdatedAt, now), cancellationToken) == 1;

    public async Task<bool> TryConsumeAsync(
        Guid otpCodeId,
        int maxAttempts,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        await dbContext.EmailOtpCodes
            .Where(otp => otp.Id == otpCodeId
                          && otp.ConsumedAt == null
                          && otp.AttemptCount < maxAttempts
                          && otp.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(otp => otp.ConsumedAt, now)
                .SetProperty(otp => otp.UpdatedAt, now), cancellationToken) == 1;

    private static bool IsActiveCodeConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ActiveCodeConstraintName
        };
}
