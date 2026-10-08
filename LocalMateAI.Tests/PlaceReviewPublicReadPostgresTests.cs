using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class PlaceReviewPublicReadPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPagedByPlace_ReturnsNewestFirst_WithReviewerNames_AndOnlyThisPlace()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new PlaceReviewRepository(c);

        var firstPage = await repository.GetPagedByPlaceAsync(seed.PlaceId, new PlaceReviewQuery { PageSize = 2 });

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(["Trần Thị B", "Nguyễn Văn A"], firstPage.Items.Select(item => item.ReviewerName));

        // Đánh giá từ chuyến đi đã xoá mềm vẫn hiển thị.
        var newest = firstPage.Items[0];
        Assert.Equal(4, newest.Rating);
        Assert.Equal([ReviewQuickTags.TooCrowded], newest.QuickTags);
        Assert.Equal("Hơi đông", newest.Comment);
        Assert.Equal(Now.AddHours(-1), newest.CreatedAt);

        var withoutComment = firstPage.Items[1];
        Assert.Equal(2, withoutComment.Rating);
        Assert.Empty(withoutComment.QuickTags);
        Assert.Null(withoutComment.Comment);

        var secondPage = await repository.GetPagedByPlaceAsync(
            seed.PlaceId, new PlaceReviewQuery { Page = 2, PageSize = 2 });

        Assert.Equal(5, Assert.Single(secondPage.Items).Rating);
    }

    [Fact]
    public async Task GetPagedByPlace_CanFilterAndSortByRating()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new PlaceReviewRepository(c);

        var fiveStars = await repository.GetPagedByPlaceAsync(seed.PlaceId, new PlaceReviewQuery { Rating = 5 });
        var lowestFirst = await repository.GetPagedByPlaceAsync(
            seed.PlaceId, new PlaceReviewQuery { SortBy = "rating", SortDirection = "asc" });
        var highestFirst = await repository.GetPagedByPlaceAsync(
            seed.PlaceId, new PlaceReviewQuery { SortBy = "RATING" });

        Assert.Equal(1, fiveStars.TotalCount);
        Assert.Equal("Nguyễn Văn A", Assert.Single(fiveStars.Items).ReviewerName);
        Assert.Equal([2, 4, 5], lowestFirst.Items.Select(item => item.Rating));
        Assert.Equal([5, 4, 2], highestFirst.Items.Select(item => item.Rating));
    }

    [Fact]
    public async Task GetSummary_CountsEveryReviewOfThePlace()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new PlaceReviewRepository(c);

        var reviewed = await repository.GetSummaryAsync(seed.PlaceId);
        var notReviewed = await repository.GetSummaryAsync(seed.PendingPlaceId);

        Assert.Equal(new PlaceReviewSummary(3, 11), reviewed);
        Assert.Equal(3.7m, reviewed.AverageRating);
        Assert.Equal(new PlaceReviewSummary(0, 0), notReviewed);
    }

    [Fact]
    public async Task IsPlaceVisible_OnlyForActivePlacesThatAreNotSoftDeleted()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new PlaceReviewRepository(c);

        Assert.True(await repository.IsPlaceVisibleAsync(seed.PlaceId));
        Assert.False(await repository.IsPlaceVisibleAsync(seed.PendingPlaceId));
        Assert.False(await repository.IsPlaceVisibleAsync(seed.InactivePlaceId));
        Assert.False(await repository.IsPlaceVisibleAsync(seed.DeletedPlaceId));
        Assert.False(await repository.IsPlaceVisibleAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheCallersReviewOfThatItem_AndUpdatesTheSummary()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var seed = await SeedAsync(c);
        var repository = new PlaceReviewRepository(c);

        // Người khác không xoá được đánh giá của A.
        Assert.Equal(0, await repository.DeleteAsync(seed.UserBId, seed.FirstItemId));
        Assert.Equal(1, await repository.DeleteAsync(seed.UserAId, seed.FirstItemId));
        Assert.Equal(0, await repository.DeleteAsync(seed.UserAId, seed.FirstItemId));

        Assert.Equal(new PlaceReviewSummary(2, 6), await repository.GetSummaryAsync(seed.PlaceId));
        Assert.Equal(3, await c.PlaceReviews.CountAsync()); // 2 của địa điểm chính + 1 của địa điểm khác
    }

    private static async Task<Seed> SeedAsync(AppDbContext c)
    {
        var place = Place("Quán chính", PlaceStatus.Active);
        var otherPlace = Place("Quán khác", PlaceStatus.Active);
        var pending = Place("Chờ duyệt", PlaceStatus.Pending);
        var inactive = Place("Đã ẩn", PlaceStatus.Inactive);
        var deleted = Place("Đã xoá", PlaceStatus.Inactive);
        deleted.DeletedAt = Now;
        c.Places.AddRange(place, otherPlace, pending, inactive, deleted);

        var roleId = await TestRoles.GetUserRoleIdAsync(c);
        var userA = User("a@test.local", "Nguyễn Văn A", roleId);
        var userB = User("b@test.local", "Trần Thị B", roleId);
        c.Users.AddRange(userA, userB);

        var firstTrip = Trip(userA.Id, place.Id, otherPlace.Id);
        var secondTrip = Trip(userA.Id, place.Id);
        var deletedTrip = Trip(userB.Id, place.Id);
        deletedTrip.DeletedAt = Now;
        c.Trips.AddRange(firstTrip, secondTrip, deletedTrip);
        await c.SaveChangesAsync();

        var reviews = new[]
        {
            (Review: Review(userA.Id, ItemAt(firstTrip, 0), 5, "Tuyệt vời", ReviewQuickTags.WorthVisiting), At: Now.AddHours(-3)),
            (Review: Review(userA.Id, ItemAt(secondTrip, 0), 2, null), At: Now.AddHours(-2)),
            (Review: Review(userB.Id, ItemAt(deletedTrip, 0), 4, "Hơi đông", ReviewQuickTags.TooCrowded), At: Now.AddHours(-1)),
            (Review: Review(userA.Id, ItemAt(firstTrip, 1), 1, "Của địa điểm khác"), At: Now)
        };
        c.PlaceReviews.AddRange(reviews.Select(entry => entry.Review));
        await c.SaveChangesAsync();

        // CreatedAt do AppDbContext tự đặt lúc lưu nên phải ghi lại để có thứ tự thời gian xác định.
        foreach (var (review, at) in reviews)
        {
            await c.PlaceReviews
                .Where(saved => saved.Id == review.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(saved => saved.CreatedAt, at));
        }

        c.ChangeTracker.Clear();
        return new Seed(
            place.Id, pending.Id, inactive.Id, deleted.Id, userA.Id, userB.Id, ItemAt(firstTrip, 0).Id);
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

    private sealed record Seed(
        Guid PlaceId,
        Guid PendingPlaceId,
        Guid InactivePlaceId,
        Guid DeletedPlaceId,
        Guid UserAId,
        Guid UserBId,
        Guid FirstItemId);
}
