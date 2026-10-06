using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class AdminFeedbackService(
    IAdminFeedbackRepository repository,
    TimeProvider timeProvider) : IAdminFeedbackService
{
    public const int LowestRatedPlaceLimit = 5;

    public Task<PagedResult<AdminReviewResponse>> GetReviewsAsync(
        AdminReviewQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return repository.GetReviewsAsync(query, ResolveBounds(query), cancellationToken);
    }

    public Task<PagedResult<AdminTripFeedbackResponse>> GetTripFeedbackAsync(
        AdminTripFeedbackQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return repository.GetTripFeedbackAsync(query, ResolveBounds(query), cancellationToken);
    }

    public async Task<AdminFeedbackSummaryResponse> GetSummaryAsync(
        DashboardDateRangeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var range = DashboardDateRules.Resolve(query, DashboardDateRules.Today(timeProvider))
            ?? throw new ArgumentException("Khoảng ngày chưa qua kiểm tra hợp lệ.", nameof(query));
        var data = await repository.GetSummaryDataAsync(
            range.StartUtc, range.EndUtc, LowestRatedPlaceLimit, cancellationToken);

        var reviewCount = data.ReviewCountsByRating.Values.Sum();
        var ratingSum = data.ReviewCountsByRating.Sum(entry => entry.Key * entry.Value);

        // Luôn đủ 5 mức sao (5 → 1), mức không có lượt nào thì 0.
        var ratings = Enumerable
            .Range(PlaceReviewService.MinRating, PlaceReviewService.MaxRating - PlaceReviewService.MinRating + 1)
            .Reverse()
            .Select(rating => new RatingCountResponse(rating, data.ReviewCountsByRating.GetValueOrDefault(rating)))
            .ToList();

        // Đủ mọi mã, nhiều lượt trước; OrderByDescending giữ nguyên thứ tự khai báo giữa các mã bằng nhau.
        var reviewQuickTags = ReviewQuickTags.All
            .Select(code => new ReviewQuickTagCountResponse(code, data.ReviewQuickTagCounts.GetValueOrDefault(code)))
            .OrderByDescending(tag => tag.Count)
            .ToList();
        var feedbackQuickTags = Enum.GetValues<FeedbackQuickTag>()
            .Select(tag => new TripFeedbackQuickTagCountResponse(
                tag, data.TripFeedbackQuickTagCounts.GetValueOrDefault(tag)))
            .OrderByDescending(tag => tag.Count)
            .ToList();

        var lowestRated = data.LowestRatedPlaces
            .Select(place => new LowRatedPlaceResponse(
                place.PlaceId,
                place.Name,
                place.IsVisible,
                new PlaceReviewSummary(place.ReviewCount, place.RatingSum).AverageRating!.Value,
                place.ReviewCount))
            .ToList();

        return new AdminFeedbackSummaryResponse(
            range.From,
            range.To,
            new AdminReviewSummaryResponse(
                reviewCount,
                new PlaceReviewSummary(reviewCount, ratingSum).AverageRating,
                ratings,
                reviewQuickTags),
            new AdminTripFeedbackSummaryResponse(feedbackQuickTags.Sum(tag => tag.Count), feedbackQuickTags),
            lowestRated,
            timeProvider.GetUtcNow().UtcDateTime);
    }

    // Ngày theo giờ Việt Nam, tính cả hai đầu ⇒ khoảng UTC nửa mở [00:00 ngày From, 00:00 ngày sau To).
    // Chuỗi sai định dạng đã bị validator chặn ở controller.
    private static AdminFeedbackDateBounds ResolveBounds(AdminFeedbackListQuery query) =>
        new(
            DashboardDateRules.TryParseDate(query.From, out var from)
                ? DashboardDateRules.StartOfDayUtc(from)
                : null,
            DashboardDateRules.TryParseDate(query.To, out var to)
                ? DashboardDateRules.StartOfDayUtc(to.AddDays(1))
                : null);
}
