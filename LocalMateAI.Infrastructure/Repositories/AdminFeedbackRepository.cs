using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminFeedbackRepository(AppDbContext dbContext) : IAdminFeedbackRepository
{
    private static readonly SortMap<ReviewRow> ReviewSortMap =
        new SortMap<ReviewRow>("createdAt", true, row => row.Id)
            .Add("createdAt", row => row.CreatedAt)
            .Add("rating", row => row.Rating);

    private static readonly SortMap<FeedbackRow> FeedbackSortMap =
        new SortMap<FeedbackRow>("createdAt", true, row => row.Id)
            .Add("createdAt", row => row.CreatedAt);

    public async Task<PagedResult<AdminReviewResponse>> GetReviewsAsync(
        AdminReviewQuery query,
        AdminFeedbackDateBounds bounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(bounds);

        // Không lọc địa điểm ẩn hay chuyến đã xoá mềm: admin xem mọi đánh giá, kèm cờ cho biết.
        var rows =
            from review in dbContext.PlaceReviews.AsNoTracking()
            join user in dbContext.Users on review.UserId equals user.Id
            join place in dbContext.Places on review.PlaceId equals place.Id
            join item in dbContext.ItineraryItems on review.ItineraryItemId equals item.Id
            select new ReviewRow
            {
                Id = review.Id,
                Rating = review.Rating,
                QuickTags = review.QuickTags,
                Comment = review.Comment,
                CreatedAt = review.CreatedAt,
                UserId = user.Id,
                UserFullName = user.FullName,
                UserEmail = user.Email,
                PlaceId = place.Id,
                PlaceName = place.Name,
                PlaceVisible = place.Status == PlaceStatus.Active && place.DeletedAt == null,
                TripId = item.TripId,
                TripDeleted = item.Trip.DeletedAt != null
            };

        if (query.PlaceId is { } placeId)
        {
            rows = rows.Where(row => row.PlaceId == placeId);
        }

        if (query.UserId is { } userId)
        {
            rows = rows.Where(row => row.UserId == userId);
        }

        if (query.Rating is { } rating)
        {
            rows = rows.Where(row => row.Rating == rating);
        }

        if (bounds.FromUtc is { } fromUtc)
        {
            rows = rows.Where(row => row.CreatedAt >= fromUtc);
        }

        if (bounds.ToUtcExclusive is { } toUtc)
        {
            rows = rows.Where(row => row.CreatedAt < toUtc);
        }

        if (query.HasComment is { } hasComment)
        {
            rows = hasComment
                ? rows.Where(row => row.Comment != null && row.Comment.Trim() != "")
                : rows.Where(row => row.Comment == null || row.Comment.Trim() == "");
        }

        if (query.NormalizedSearch is { } search)
        {
            var pattern = LikePattern(search);
            rows = rows.Where(row =>
                (row.Comment != null
                 && EF.Functions.ILike(EF.Functions.Unaccent(row.Comment), EF.Functions.Unaccent(pattern), "\\"))
                || EF.Functions.ILike(EF.Functions.Unaccent(row.PlaceName), EF.Functions.Unaccent(pattern), "\\")
                || EF.Functions.ILike(EF.Functions.Unaccent(row.UserFullName), EF.Functions.Unaccent(pattern), "\\")
                || EF.Functions.ILike(EF.Functions.Unaccent(row.UserEmail), EF.Functions.Unaccent(pattern), "\\"));
        }

        var page = await rows.ApplySort(query, ReviewSortMap).ToPagedResultAsync(query, cancellationToken);
        var items = page.Items
            .Select(row => new AdminReviewResponse(
                row.Id,
                row.Rating,
                row.QuickTags,
                row.Comment,
                row.CreatedAt,
                new AdminFeedbackAuthorResponse(row.UserId, row.UserFullName, row.UserEmail),
                new AdminReviewPlaceResponse(row.PlaceId, row.PlaceName, row.PlaceVisible),
                row.TripId,
                row.TripDeleted))
            .ToList();

        return PagedResult<AdminReviewResponse>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<PagedResult<AdminTripFeedbackResponse>> GetTripFeedbackAsync(
        AdminTripFeedbackQuery query,
        AdminFeedbackDateBounds bounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(bounds);

        var rows =
            from feedback in dbContext.Feedbacks.AsNoTracking()
            join user in dbContext.Users on feedback.UserId equals user.Id
            join trip in dbContext.Trips on feedback.TripId equals trip.Id
            select new FeedbackRow
            {
                Id = feedback.Id,
                TripId = trip.Id,
                TripDeleted = trip.DeletedAt != null,
                QuickTag = feedback.QuickTag,
                Comment = feedback.Comment,
                CreatedAt = feedback.CreatedAt,
                UserId = user.Id,
                UserFullName = user.FullName,
                UserEmail = user.Email
            };

        if (query.UserId is { } userId)
        {
            rows = rows.Where(row => row.UserId == userId);
        }

        if (query.QuickTag is { } quickTag)
        {
            rows = rows.Where(row => row.QuickTag == quickTag);
        }

        if (bounds.FromUtc is { } fromUtc)
        {
            rows = rows.Where(row => row.CreatedAt >= fromUtc);
        }

        if (bounds.ToUtcExclusive is { } toUtc)
        {
            rows = rows.Where(row => row.CreatedAt < toUtc);
        }

        if (query.HasComment is { } hasComment)
        {
            rows = hasComment
                ? rows.Where(row => row.Comment != null && row.Comment.Trim() != "")
                : rows.Where(row => row.Comment == null || row.Comment.Trim() == "");
        }

        if (query.NormalizedSearch is { } search)
        {
            var pattern = LikePattern(search);
            rows = rows.Where(row =>
                (row.Comment != null
                 && EF.Functions.ILike(EF.Functions.Unaccent(row.Comment), EF.Functions.Unaccent(pattern), "\\"))
                || EF.Functions.ILike(EF.Functions.Unaccent(row.UserFullName), EF.Functions.Unaccent(pattern), "\\")
                || EF.Functions.ILike(EF.Functions.Unaccent(row.UserEmail), EF.Functions.Unaccent(pattern), "\\"));
        }

        var page = await rows.ApplySort(query, FeedbackSortMap).ToPagedResultAsync(query, cancellationToken);
        var items = page.Items
            .Select(row => new AdminTripFeedbackResponse(
                row.Id,
                row.TripId,
                row.TripDeleted,
                row.QuickTag,
                row.Comment,
                row.CreatedAt,
                new AdminFeedbackAuthorResponse(row.UserId, row.UserFullName, row.UserEmail)))
            .ToList();

        return PagedResult<AdminTripFeedbackResponse>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<AdminFeedbackSummaryData> GetSummaryDataAsync(
        DateTime startUtc,
        DateTime endUtc,
        int lowestRatedLimit,
        CancellationToken cancellationToken = default)
    {
        var reviews = dbContext.PlaceReviews
            .AsNoTracking()
            .Where(review => review.CreatedAt >= startUtc && review.CreatedAt < endUtc);
        var feedbacks = dbContext.Feedbacks
            .AsNoTracking()
            .Where(feedback => feedback.CreatedAt >= startUtc && feedback.CreatedAt < endUtc);

        var ratingCounts = await reviews
            .GroupBy(review => review.Rating)
            .Select(group => new { Rating = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Rating, row => row.Count, cancellationToken);

        // Tách mảng QuickTags thành từng dòng (unnest) rồi đếm theo mã.
        var reviewTagCounts = await reviews
            .SelectMany(review => review.QuickTags)
            .GroupBy(tag => tag)
            .Select(group => new { Tag = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Tag, row => row.Count, cancellationToken);

        var feedbackTagCounts = await feedbacks
            .GroupBy(feedback => feedback.QuickTag)
            .Select(group => new { QuickTag = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.QuickTag, row => row.Count, cancellationToken);

        // Điểm trung bình thấp nhất trước; bằng điểm thì nơi nhiều đánh giá hơn đứng trước.
        var placeStats = reviews
            .GroupBy(review => review.PlaceId)
            .Select(group => new
            {
                PlaceId = group.Key,
                Count = group.Count(),
                Sum = group.Sum(review => review.Rating)
            });
        var lowestRated = await (
                from stat in placeStats
                join place in dbContext.Places on stat.PlaceId equals place.Id
                orderby (double)stat.Sum / stat.Count, stat.Count descending, place.Name, place.Id
                select new LowRatedPlaceRow(
                    place.Id,
                    place.Name,
                    place.Status == PlaceStatus.Active && place.DeletedAt == null,
                    stat.Count,
                    stat.Sum))
            .Take(lowestRatedLimit)
            .ToListAsync(cancellationToken);

        return new AdminFeedbackSummaryData(ratingCounts, reviewTagCounts, feedbackTagCounts, lowestRated);
    }

    private static string LikePattern(string search) =>
        "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    // Khởi tạo bằng object initializer (không dùng constructor) để EF dịch được Where/OrderBy trên các field sau Select.
    private sealed class ReviewRow
    {
        public Guid Id { get; init; }
        public int Rating { get; init; }
        public string[] QuickTags { get; init; } = [];
        public string? Comment { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid UserId { get; init; }
        public string UserFullName { get; init; } = string.Empty;
        public string UserEmail { get; init; } = string.Empty;
        public Guid PlaceId { get; init; }
        public string PlaceName { get; init; } = string.Empty;
        public bool PlaceVisible { get; init; }
        public Guid TripId { get; init; }
        public bool TripDeleted { get; init; }
    }

    private sealed class FeedbackRow
    {
        public Guid Id { get; init; }
        public Guid TripId { get; init; }
        public bool TripDeleted { get; init; }
        public FeedbackQuickTag QuickTag { get; init; }
        public string? Comment { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid UserId { get; init; }
        public string UserFullName { get; init; } = string.Empty;
        public string UserEmail { get; init; } = string.Empty;
    }
}
