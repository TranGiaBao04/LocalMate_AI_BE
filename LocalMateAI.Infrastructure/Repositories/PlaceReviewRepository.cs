using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PlaceReviewRepository(AppDbContext dbContext) : IPlaceReviewRepository
{
    private const string UserItemConstraintName = "UX_PlaceReviews_UserId_ItineraryItemId";

    public Task<OwnedItemForReviewReadModel?> GetOwnedItemAsync(
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.ItineraryItems
            .AsNoTracking()
            .Where(item => item.Id == itemId
                           && item.Trip.UserId == userId
                           && item.Trip.DeletedAt == null)
            .Select(item => new OwnedItemForReviewReadModel(item.Id, item.PlaceId, item.IsVisited))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<PlaceReview?> GetAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        dbContext.PlaceReviews
            .AsNoTracking()
            .SingleOrDefaultAsync(
                review => review.UserId == userId && review.ItineraryItemId == itemId,
                cancellationToken);

    public Task<bool> ExistsAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        dbContext.PlaceReviews
            .AsNoTracking()
            .AnyAsync(
                review => review.UserId == userId && review.ItineraryItemId == itemId,
                cancellationToken);

    public async Task<bool> TryAddAsync(
        PlaceReview review,
        CancellationToken cancellationToken = default)
    {
        dbContext.PlaceReviews.Add(review);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateReviewViolation(exception))
        {
            dbContext.Entry(review).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsDuplicateReviewViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(
            postgresException.ConstraintName,
            UserItemConstraintName,
            StringComparison.Ordinal);
}
