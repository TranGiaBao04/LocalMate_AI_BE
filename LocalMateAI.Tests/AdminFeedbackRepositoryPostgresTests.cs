using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AdminFeedbackRepositoryPostgresTests
{
    // 10:00 ngày 06/10 giờ Việt Nam.
    private static readonly DateTime Now = new(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastMinuteOfOct4 = new(2026, 10, 4, 16, 59, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstMinuteOfOct5 = new(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc);
    private static readonly AdminFeedbackDateBounds NoBounds = new(null, null);

    [Fact]
    public async Task GetReviews_ReturnsEveryReview_WithAuthorPlaceAndTripFlags()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new AdminFeedbackRepository(c);

        var page = await repository.GetReviewsAsync(new AdminReviewQuery(), NoBounds);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([4, 2, 5], page.Items.Select(item => item.Rating));

        // Đánh giá của B: chuyến đã xoá mềm, địa điểm vẫn đang hiện.
        var newest = page.Items[0];
        Assert.Equal(new AdminFeedbackAuthorResponse(seed.UserBId, "Trần Thị B", "b@test.local"), newest.User);
        Assert.Equal(new AdminReviewPlaceResponse(seed.VisiblePlaceId, "Cà phê Sữa Đá", true), newest.Place);
        Assert.True(newest.TripDeleted);
        Assert.Equal([ReviewQuickTags.TooCrowded], newest.QuickTags);
        Assert.Equal("Hơi đông", newest.Comment);
        Assert.Equal(Now, newest.CreatedAt);

        // Đánh giá trên địa điểm đã ẩn vẫn hiện cho admin, kèm cờ.
        var onHiddenPlace = page.Items[1];
        Assert.Equal(new AdminReviewPlaceResponse(seed.HiddenPlaceId, "Quán đã ẩn", false), onHiddenPlace.Place);
        Assert.Equal(seed.TripAId, onHiddenPlace.TripId);
        Assert.False(onHiddenPlace.TripDeleted);
        Assert.Null(onHiddenPlace.Comment);
    }

    [Fact]
    public async Task GetReviews_AppliesEachFilter()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new AdminFeedbackRepository(c);

        async Task<IEnumerable<int>> RatingsAsync(AdminReviewQuery query, AdminFeedbackDateBounds? bounds = null) =>
            (await repository.GetReviewsAsync(query, bounds ?? NoBounds)).Items.Select(item => item.Rating).ToList();

        Assert.Equal([4, 5], await RatingsAsync(new AdminReviewQuery { PlaceId = seed.VisiblePlaceId }));
        Assert.Equal([2, 5], await RatingsAsync(new AdminReviewQuery { UserId = seed.UserAId }));
        Assert.Equal([2], await RatingsAsync(new AdminReviewQuery { Rating = 2 }));
        Assert.Equal([4, 5], await RatingsAsync(new AdminReviewQuery { HasComment = true }));
        Assert.Equal([2], await RatingsAsync(new AdminReviewQuery { HasComment = false }));
        Assert.Equal([2, 4, 5], await RatingsAsync(new AdminReviewQuery { SortBy = "rating", SortDirection = "asc" }));

        // Trọn ngày 05/10 giờ VN: lấy dòng 00:00 ngày 05, bỏ dòng 23:59 ngày 04 và dòng ngày 06.
        var october5 = new AdminFeedbackDateBounds(FirstMinuteOfOct5, FirstMinuteOfOct5.AddDays(1));
        Assert.Equal([2], await RatingsAsync(new AdminReviewQuery(), october5));
    }

    [Fact]
    public async Task GetReviews_SearchIgnoresAccents_AcrossCommentPlaceAndAuthor()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        await SeedAsync(c);
        var repository = new AdminFeedbackRepository(c);

        async Task<IEnumerable<int>> RatingsAsync(string search) =>
            (await repository.GetReviewsAsync(new AdminReviewQuery { Search = search }, NoBounds))
            .Items.Select(item => item.Rating).ToList();

        Assert.Equal([4, 5], await RatingsAsync("ca phe"));   // tên địa điểm (và nhận xét của dòng 5 sao)
        Assert.Equal([4], await RatingsAsync("hoi dong"));    // nhận xét
        Assert.Equal([4], await RatingsAsync("TRAN THI"));    // tên người gửi
        Assert.Equal([2, 5], await RatingsAsync("a@test"));   // email người gửi
        Assert.Empty(await RatingsAsync("100%"));             // % là ký tự thường, không phải ký tự đại diện
    }

    [Fact]
    public async Task GetTripFeedback_ReturnsRowsWithFlags_AndAppliesFilters()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new AdminFeedbackRepository(c);

        var all = await repository.GetTripFeedbackAsync(new AdminTripFeedbackQuery(), NoBounds);

        Assert.Equal(2, all.TotalCount);
        var newest = all.Items[0];
        Assert.Equal(FeedbackQuickTag.TooExpensive, newest.QuickTag);
        Assert.Null(newest.Comment);
        Assert.True(newest.TripDeleted);
        Assert.Equal(new AdminFeedbackAuthorResponse(seed.UserBId, "Trần Thị B", "b@test.local"), newest.User);
        var oldest = all.Items[1];
        Assert.Equal(seed.TripAId, oldest.TripId);
        Assert.False(oldest.TripDeleted);
        Assert.Equal("Lịch hợp lý", oldest.Comment);

        async Task<IEnumerable<FeedbackQuickTag>> TagsAsync(AdminTripFeedbackQuery query, AdminFeedbackDateBounds? bounds = null) =>
            (await repository.GetTripFeedbackAsync(query, bounds ?? NoBounds)).Items.Select(item => item.QuickTag).ToList();

        Assert.Equal([FeedbackQuickTag.Suitable], await TagsAsync(new AdminTripFeedbackQuery { QuickTag = FeedbackQuickTag.Suitable }));
        Assert.Equal([FeedbackQuickTag.Suitable], await TagsAsync(new AdminTripFeedbackQuery { UserId = seed.UserAId }));
        Assert.Equal([FeedbackQuickTag.Suitable], await TagsAsync(new AdminTripFeedbackQuery { HasComment = true }));
        Assert.Equal([FeedbackQuickTag.TooExpensive], await TagsAsync(new AdminTripFeedbackQuery { HasComment = false }));
        Assert.Equal([FeedbackQuickTag.Suitable], await TagsAsync(new AdminTripFeedbackQuery { Search = "hop ly" }));
        Assert.Equal([FeedbackQuickTag.TooExpensive], await TagsAsync(new AdminTripFeedbackQuery { Search = "b@test" }));
        Assert.Equal(
            [FeedbackQuickTag.TooExpensive],
            await TagsAsync(new AdminTripFeedbackQuery(), new AdminFeedbackDateBounds(Now, null)));
    }

    [Fact]
    public async Task GetSummaryData_CountsOnlyRowsInRange_AndRanksLowestRatedPlaces()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);

        // Thêm một địa điểm có 2 đánh giá 2 sao: cùng điểm 2,0 với "Quán đã ẩn" nhưng nhiều lượt hơn.
        var twice = Place("Quán hai lượt", PlaceStatus.Active);
        c.Places.Add(twice);
        var extraTrip = Trip(seed.UserAId, twice.Id, twice.Id);
        c.Trips.Add(extraTrip);
        await c.SaveChangesAsync();
        c.PlaceReviews.AddRange(
            Review(seed.UserAId, ItemAt(extraTrip, 0), 2, null, ReviewQuickTags.TooCrowded),
            Review(seed.UserAId, ItemAt(extraTrip, 1), 2, null, ReviewQuickTags.TooCrowded, ReviewQuickTags.Overpriced));
        await c.SaveChangesAsync();
        await c.PlaceReviews
            .Where(saved => saved.PlaceId == twice.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(saved => saved.CreatedAt, Now));
        c.ChangeTracker.Clear();
        var repository = new AdminFeedbackRepository(c);

        var all = await repository.GetSummaryDataAsync(LastMinuteOfOct4, Now.AddMinutes(1), 5);

        Assert.Equal(3, all.ReviewCountsByRating[2]);
        Assert.Equal(1, all.ReviewCountsByRating[4]);
        Assert.Equal(1, all.ReviewCountsByRating[5]);
        Assert.Equal(3, all.ReviewCountsByRating.Count);
        Assert.Equal(3, all.ReviewQuickTagCounts[ReviewQuickTags.TooCrowded]);
        Assert.Equal(1, all.ReviewQuickTagCounts[ReviewQuickTags.WorthVisiting]);
        Assert.Equal(1, all.ReviewQuickTagCounts[ReviewQuickTags.Overpriced]);
        Assert.Equal(1, all.TripFeedbackQuickTagCounts[FeedbackQuickTag.Suitable]);
        Assert.Equal(1, all.TripFeedbackQuickTagCounts[FeedbackQuickTag.TooExpensive]);
        Assert.Equal(
            [
                new LowRatedPlaceRow(twice.Id, "Quán hai lượt", true, 2, 4),
                new LowRatedPlaceRow(seed.HiddenPlaceId, "Quán đã ẩn", false, 1, 2),
                new LowRatedPlaceRow(seed.VisiblePlaceId, "Cà phê Sữa Đá", true, 2, 9)
            ],
            all.LowestRatedPlaces);

        var topOne = await repository.GetSummaryDataAsync(LastMinuteOfOct4, Now.AddMinutes(1), 1);
        Assert.Equal("Quán hai lượt", Assert.Single(topOne.LowestRatedPlaces).Name);

        // Trọn ngày 05/10 giờ VN: chỉ còn đánh giá 2 sao trên địa điểm đã ẩn và phản hồi của A.
        var october5 = await repository.GetSummaryDataAsync(FirstMinuteOfOct5, FirstMinuteOfOct5.AddDays(1), 5);
        Assert.Equal(new Dictionary<int, int> { [2] = 1 }, october5.ReviewCountsByRating);
        Assert.Empty(october5.ReviewQuickTagCounts);
        Assert.Equal(
            new Dictionary<FeedbackQuickTag, int> { [FeedbackQuickTag.Suitable] = 1 },
            october5.TripFeedbackQuickTagCounts);
        Assert.Equal("Quán đã ẩn", Assert.Single(october5.LowestRatedPlaces).Name);
    }

    private static async Task<Seed> SeedAsync(AppDbContext c)
    {
        var visible = Place("Cà phê Sữa Đá", PlaceStatus.Active);
        var hidden = Place("Quán đã ẩn", PlaceStatus.Inactive);
        c.Places.AddRange(visible, hidden);

        var roleId = await TestRoles.GetUserRoleIdAsync(c);
        var userA = User("a@test.local", "Nguyễn Văn A", roleId);
        var userB = User("b@test.local", "Trần Thị B", roleId);
        c.Users.AddRange(userA, userB);

        var tripA = Trip(userA.Id, visible.Id, hidden.Id);
        var tripB = Trip(userB.Id, visible.Id);
        tripB.DeletedAt = Now;
        c.Trips.AddRange(tripA, tripB);
        await c.SaveChangesAsync();

        var reviews = new[]
        {
            (Review: Review(userA.Id, ItemAt(tripA, 0), 5, "Cà phê ngon", ReviewQuickTags.WorthVisiting), At: LastMinuteOfOct4),
            (Review: Review(userA.Id, ItemAt(tripA, 1), 2, null), At: FirstMinuteOfOct5),
            (Review: Review(userB.Id, ItemAt(tripB, 0), 4, "Hơi đông", ReviewQuickTags.TooCrowded), At: Now)
        };
        c.PlaceReviews.AddRange(reviews.Select(entry => entry.Review));

        var feedbackA = new Feedback
        {
            UserId = userA.Id, TripId = tripA.Id, QuickTag = FeedbackQuickTag.Suitable, Comment = "Lịch hợp lý"
        };
        var feedbackB = new Feedback
        {
            UserId = userB.Id, TripId = tripB.Id, QuickTag = FeedbackQuickTag.TooExpensive, Comment = null
        };
        c.Feedbacks.AddRange(feedbackA, feedbackB);
        await c.SaveChangesAsync();

        // CreatedAt do AppDbContext tự đặt lúc lưu nên phải ghi lại để có thứ tự thời gian xác định.
        foreach (var (review, at) in reviews)
        {
            await c.PlaceReviews
                .Where(saved => saved.Id == review.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(saved => saved.CreatedAt, at));
        }

        await c.Feedbacks.Where(saved => saved.Id == feedbackA.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(saved => saved.CreatedAt, FirstMinuteOfOct5));
        await c.Feedbacks.Where(saved => saved.Id == feedbackB.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(saved => saved.CreatedAt, Now));

        c.ChangeTracker.Clear();
        return new Seed(userA.Id, userB.Id, visible.Id, hidden.Id, tripA.Id);
    }

    private static Place Place(string name, PlaceStatus status) => new()
    {
        Name = name,
        Address = "Test",
        Location = new Point(106.70, 10.77) { SRID = 4326 },
        Category = PlaceCategory.Cafe,
        Status = status
    };

    private static User User(string email, string fullName, Guid roleId) => new()
    {
        Email = email,
        FullName = fullName,
        PasswordHash = "hash",
        RoleId = roleId
    };

    private static Trip Trip(Guid userId, params Guid[] placeIds) => new()
    {
        UserId = userId,
        StartLatitude = 10.77,
        StartLongitude = 106.70,
        DurationHours = 4,
        BudgetMax = 500_000,
        Status = TripStatus.Finalized,
        Items = placeIds
            .Select((placeId, index) => new ItineraryItem
            {
                PlaceId = placeId,
                OrderIndex = index,
                ScheduledTime = new TimeOnly(9 + index * 2, 0),
                EstimatedDurationMinutes = 60,
                EstimatedBudget = 50_000
            })
            .ToList()
    };

    private static ItineraryItem ItemAt(Trip trip, int orderIndex) =>
        trip.Items.Single(item => item.OrderIndex == orderIndex);

    private static PlaceReview Review(
        Guid userId, ItineraryItem item, int rating, string? comment, params string[] quickTags) => new()
    {
        UserId = userId,
        PlaceId = item.PlaceId,
        ItineraryItemId = item.Id,
        Rating = rating,
        QuickTags = quickTags,
        Comment = comment
    };

    private sealed record Seed(Guid UserAId, Guid UserBId, Guid VisiblePlaceId, Guid HiddenPlaceId, Guid TripAId);
}
