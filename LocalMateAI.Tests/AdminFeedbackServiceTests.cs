using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminFeedbackServiceTests
{
    // 10:00 ngày 06/10/2026 giờ Việt Nam.
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reviews_ConvertVietnamDatesToAHalfOpenUtcRange()
    {
        var repository = new FakeRepository();
        var query = new AdminReviewQuery { From = "2026-10-05", To = "2026-10-06" };

        await Service(repository).GetReviewsAsync(query);

        var (passedQuery, bounds) = Assert.Single(repository.ReviewCalls);
        Assert.Same(query, passedQuery);
        // 00:00 ngày 05/10 giờ VN = 17:00 ngày 04/10 UTC; hết ngày 06/10 giờ VN = 17:00 ngày 06/10 UTC.
        Assert.Equal(new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc), bounds.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc), bounds.ToUtcExclusive);
        Assert.Equal(DateTimeKind.Utc, bounds.FromUtc!.Value.Kind);
    }

    [Fact]
    public async Task TripFeedback_WithoutDates_HasNoBounds()
    {
        var repository = new FakeRepository();
        var query = new AdminTripFeedbackQuery();

        await Service(repository).GetTripFeedbackAsync(query);

        var (passedQuery, bounds) = Assert.Single(repository.FeedbackCalls);
        Assert.Same(query, passedQuery);
        Assert.Equal(new AdminFeedbackDateBounds(null, null), bounds);
    }

    [Fact]
    public async Task TripFeedback_WithOnlyTo_BoundsOnlyTheEnd()
    {
        var repository = new FakeRepository();

        await Service(repository).GetTripFeedbackAsync(new AdminTripFeedbackQuery { To = "2026-10-06" });

        var (_, bounds) = Assert.Single(repository.FeedbackCalls);
        Assert.Null(bounds.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc), bounds.ToUtcExclusive);
    }

    [Fact]
    public async Task Summary_DefaultsToLast30VietnamDays_AndAsksForFivePlaces()
    {
        var repository = new FakeRepository();

        var result = await Service(repository).GetSummaryAsync(new DashboardDateRangeQuery());

        Assert.Equal(new DateOnly(2026, 9, 7), result.From);
        Assert.Equal(new DateOnly(2026, 10, 6), result.To);
        Assert.Equal(Now.UtcDateTime, result.GeneratedAt);
        var call = Assert.Single(repository.SummaryCalls);
        Assert.Equal(new DateTime(2026, 9, 6, 17, 0, 0, DateTimeKind.Utc), call.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Utc), call.EndUtc);
        Assert.Equal(5, call.Limit);
    }

    [Fact]
    public async Task Summary_WithoutAnyData_ReturnsZeroesForEveryRatingAndTag()
    {
        var result = await Service(new FakeRepository()).GetSummaryAsync(new DashboardDateRangeQuery());

        Assert.Equal(0, result.Reviews.Count);
        Assert.Null(result.Reviews.AverageRating);
        Assert.Equal([5, 4, 3, 2, 1], result.Reviews.Ratings.Select(rating => rating.Rating));
        Assert.All(result.Reviews.Ratings, rating => Assert.Equal(0, rating.Count));
        Assert.Equal(ReviewQuickTags.All, result.Reviews.QuickTags.Select(tag => tag.Code));
        Assert.Equal(0, result.TripFeedback.Count);
        Assert.Equal(Enum.GetValues<FeedbackQuickTag>(), result.TripFeedback.QuickTags.Select(tag => tag.QuickTag));
        Assert.Empty(result.LowestRatedPlaces);
    }

    [Fact]
    public async Task Summary_BuildsCountsAverageAndRankings()
    {
        var placeId = Guid.NewGuid();
        var repository = new FakeRepository
        {
            Summary = new AdminFeedbackSummaryData(
                new Dictionary<int, int> { [5] = 6, [4] = 4, [2] = 2 },
                new Dictionary<string, int> { [ReviewQuickTags.TooCrowded] = 3, [ReviewQuickTags.WorthVisiting] = 7 },
                new Dictionary<FeedbackQuickTag, int> { [FeedbackQuickTag.TooExpensive] = 2, [FeedbackQuickTag.Suitable] = 3 },
                [new LowRatedPlaceRow(placeId, "Quán điểm thấp", false, 3, 7)])
        };

        var result = await Service(repository).GetSummaryAsync(
            new DashboardDateRangeQuery { From = "2026-10-01", To = "2026-10-06" });

        Assert.Equal(12, result.Reviews.Count);
        Assert.Equal(4.2m, result.Reviews.AverageRating); // (30 + 16 + 4) / 12 = 4,1666…
        Assert.Equal(
            [new RatingCountResponse(5, 6), new(4, 4), new(3, 0), new(2, 2), new(1, 0)],
            result.Reviews.Ratings);
        Assert.Equal(
            [new ReviewQuickTagCountResponse(ReviewQuickTags.WorthVisiting, 7), new(ReviewQuickTags.TooCrowded, 3)],
            result.Reviews.QuickTags.Take(2));
        Assert.Equal(ReviewQuickTags.All.Count, result.Reviews.QuickTags.Count);

        Assert.Equal(5, result.TripFeedback.Count);
        Assert.Equal(
            [new TripFeedbackQuickTagCountResponse(FeedbackQuickTag.Suitable, 3), new(FeedbackQuickTag.TooExpensive, 2)],
            result.TripFeedback.QuickTags.Take(2));

        Assert.Equal(
            new LowRatedPlaceResponse(placeId, "Quán điểm thấp", false, 2.3m, 3),
            Assert.Single(result.LowestRatedPlaces));
    }

    private static AdminFeedbackService Service(FakeRepository repository) =>
        new(repository, new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeRepository : IAdminFeedbackRepository
    {
        public AdminFeedbackSummaryData Summary { get; init; } = new(
            new Dictionary<int, int>(),
            new Dictionary<string, int>(),
            new Dictionary<FeedbackQuickTag, int>(),
            []);

        public List<(AdminReviewQuery Query, AdminFeedbackDateBounds Bounds)> ReviewCalls { get; } = [];

        public List<(AdminTripFeedbackQuery Query, AdminFeedbackDateBounds Bounds)> FeedbackCalls { get; } = [];

        public List<(DateTime StartUtc, DateTime EndUtc, int Limit)> SummaryCalls { get; } = [];

        public Task<PagedResult<AdminReviewResponse>> GetReviewsAsync(
            AdminReviewQuery query,
            AdminFeedbackDateBounds bounds,
            CancellationToken cancellationToken = default)
        {
            ReviewCalls.Add((query, bounds));
            return Task.FromResult(PagedResult<AdminReviewResponse>.Create([], 1, 20, 0));
        }

        public Task<PagedResult<AdminTripFeedbackResponse>> GetTripFeedbackAsync(
            AdminTripFeedbackQuery query,
            AdminFeedbackDateBounds bounds,
            CancellationToken cancellationToken = default)
        {
            FeedbackCalls.Add((query, bounds));
            return Task.FromResult(PagedResult<AdminTripFeedbackResponse>.Create([], 1, 20, 0));
        }

        public Task<AdminFeedbackSummaryData> GetSummaryDataAsync(
            DateTime startUtc,
            DateTime endUtc,
            int lowestRatedLimit,
            CancellationToken cancellationToken = default)
        {
            SummaryCalls.Add((startUtc, endUtc, lowestRatedLimit));
            return Task.FromResult(Summary);
        }
    }
}
