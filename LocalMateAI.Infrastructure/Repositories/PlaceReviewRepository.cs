using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PlaceReviewRepository(AppDbContext dbContext) : IPlaceReviewRepository
{
    private const string UserItemConstraintName = "UX_PlaceReviews_UserId_ItineraryItemId";

    private static readonly SortMap<PlaceReview> PublicReviewSortMap =
        new SortMap<PlaceReview>("createdAt", true, review => review.Id)
            .Add("createdAt", review => review.CreatedAt)
            .Add("rating", review => review.Rating);

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

    public Task<int> DeleteAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        dbContext.PlaceReviews
            .Where(review => review.UserId == userId && review.ItineraryItemId == itemId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<bool> IsPlaceVisibleAsync(
        Guid placeId,
        CancellationToken cancellationToken = default) =>
        dbContext.Places
            .AsNoTracking()
            .AnyAsync(
                place => place.Id == placeId
                         && place.Status == PlaceStatus.Active
                         && place.DeletedAt == null,
                cancellationToken);

    public async Task<PlaceReviewSummary> GetSummaryAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var stats = await dbContext.PlaceReviews
            .AsNoTracking()
            .Where(review => review.PlaceId == placeId)
            .GroupBy(review => review.PlaceId)
            .Select(group => new { Count = group.Count(), Sum = group.Sum(review => review.Rating) })
            .SingleOrDefaultAsync(cancellationToken);

        return stats is null
            ? new PlaceReviewSummary(0, 0)
            : new PlaceReviewSummary(stats.Count, stats.Sum);
    }

    public Task<PagedResult<PublicPlaceReviewResponse>> GetPagedByPlaceAsync(
        Guid placeId,
        PlaceReviewQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var reviews = dbContext.PlaceReviews
            .AsNoTracking()
            .Where(review => review.PlaceId == placeId);

        if (query.Rating is { } rating)
        {
            reviews = reviews.Where(review => review.Rating == rating);
        }

        // PlaceReview không có navigation tới User nên lấy tên bằng truy vấn con; FK bảo đảm user tồn tại.
        return reviews
            .ApplySort(query, PublicReviewSortMap)
            .Select(review => new PublicPlaceReviewResponse(
                review.Id,
                review.Rating,
                review.QuickTags,
                review.Comment,
                review.CreatedAt,
                dbContext.Users
                    .Where(user => user.Id == review.UserId)
                    .Select(user => user.FullName)
                    .First()))
            .ToPagedResultAsync(query, cancellationToken);
    }

    private static bool IsDuplicateReviewViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(
            postgresException.ConstraintName,
            UserItemConstraintName,
            StringComparison.Ordinal);
}
