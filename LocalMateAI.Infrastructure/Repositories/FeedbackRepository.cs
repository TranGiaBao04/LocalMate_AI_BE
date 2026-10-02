using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class FeedbackRepository(AppDbContext dbContext) : IFeedbackRepository
{
    private const string FeedbackUserTripConstraintName = "UX_Feedbacks_UserId_TripId";

    public Task<Trip?> GetOwnedTripAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Trips
            .AsNoTracking()
            .SingleOrDefaultAsync(
                trip => trip.Id == tripId && trip.UserId == userId && trip.DeletedAt == null,
                cancellationToken);

    public Task<bool> ExistsAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default) =>
        dbContext.Feedbacks
            .AsNoTracking()
            .AnyAsync(
                feedback => feedback.UserId == userId && feedback.TripId == tripId,
                cancellationToken);

    public async Task<bool> TryAddAsync(
        Feedback feedback,
        CancellationToken cancellationToken = default)
    {
        dbContext.Feedbacks.Add(feedback);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateFeedbackViolation(exception))
        {
            dbContext.Entry(feedback).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsDuplicateFeedbackViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(
            postgresException.ConstraintName,
            FeedbackUserTripConstraintName,
            StringComparison.Ordinal);
}
