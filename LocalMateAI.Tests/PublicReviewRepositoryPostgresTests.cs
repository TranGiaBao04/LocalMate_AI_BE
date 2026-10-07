using LocalMateAI.Application.DTOs.PublicReviews;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class PublicReviewRepositoryPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetFeatured_ReturnsOneQualifyingReviewPerUser_NewestFirst()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var repository = new PublicReviewRepository(c);

        Assert.Empty(await repository.GetFeaturedAsync(4, 12));

        var seed = await SeedAsync(c);

        var all = await repository.GetFeaturedAsync(4, 12);
        var firstTwo = await repository.GetFeaturedAsync(4, 2);

        // B (3 sao, nhận xét toàn khoảng trắng) không có thẻ; các đánh giá của C ở địa điểm đang ẩn bị loại.
        Assert.Equal(["Nguyễn Văn A", "Lê Văn C", "Phạm Thị D"], all.Select(item => item.ReviewerName));
        Assert.Equal(["Cũng ổn", "Rất đáng đi", "Sẽ quay lại"], all.Select(item => item.Comment));
        Assert.Equal(all.Take(2).Select(item => item.Id), firstTwo.Select(item => item.Id));

        // A có đánh giá 2 sao mới hơn, nhưng thẻ vẫn là đánh giá mới nhất ĐẠT điều kiện.
        var newest = all[0];
        Assert.Equal(seed.NewestQualifyingReviewId, newest.Id);
        Assert.Equal(4, newest.Rating);
        Assert.Equal(Now.AddHours(-1), newest.CreatedAt);
        Assert.Equal(new PublicReviewPlace(seed.OtherPlaceId, "Quán khác"), newest.Place);

        Assert.Equal(new PublicReviewPlace(seed.PlaceId, "Quán chính"), all[1].Place);

        // Nhãn nhanh trả đủ như đã lưu, đúng thứ tự, kể cả nhãn chê (service mới là nơi lọc).
        Assert.Equal(
            [ReviewQuickTags.NearMetro, ReviewQuickTags.TooCrowded, ReviewQuickTags.WorthVisiting],
            newest.QuickTags);
        Assert.Equal([ReviewQuickTags.GoodValue], all[1].QuickTags);
        Assert.Empty(all[2].QuickTags);
    }

    [Fact]
    public async Task GetFeatured_AppliesTheMinimumRating()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        await SeedAsync(c);
        var repository = new PublicReviewRepository(c);

        var fiveStarsOnly = await repository.GetFeaturedAsync(5, 12);

        // Với ngưỡng 5 sao, thẻ của A lùi về đánh giá 5 sao cũ hơn; D chỉ có 4 sao nên không còn thẻ.
        Assert.Equal(["Rất đáng đi", "Tuyệt vời"], fiveStarsOnly.Select(item => item.Comment));
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
        var userC = User("c@test.local", "Lê Văn C", roleId);
        var userD = User("d@test.local", "Phạm Thị D", roleId);
        c.Users.AddRange(userA, userB, userC, userD);

        var tripA = Trip(userA.Id, place.Id, otherPlace.Id);
        var secondTripA = Trip(userA.Id, place.Id);
        var tripB = Trip(userB.Id, place.Id, otherPlace.Id);
        var tripC = Trip(userC.Id, pending.Id, inactive.Id, deleted.Id, place.Id);
        var tripD = Trip(userD.Id, place.Id, otherPlace.Id);
        c.Trips.AddRange(tripA, secondTripA, tripB, tripC, tripD);
        await c.SaveChangesAsync();

        var newestQualifying = Review(
            userA.Id, ItemAt(tripA, 1), 4, "Cũng ổn",
            ReviewQuickTags.NearMetro, ReviewQuickTags.TooCrowded, ReviewQuickTags.WorthVisiting);
        var reviews = new[]
        {
            (Review: Review(userA.Id, ItemAt(tripA, 0), 5, "Tuyệt vời"), At: Now.AddHours(-5)),
            (Review: newestQualifying, At: Now.AddHours(-1)),
            (Review: Review(userA.Id, ItemAt(secondTripA, 0), 2, "Lần này tệ"), At: Now),
            (Review: Review(userB.Id, ItemAt(tripB, 0), 3, "Tạm được"), At: Now.AddMinutes(-10)),
            (Review: Review(userB.Id, ItemAt(tripB, 1), 5, "   "), At: Now.AddMinutes(-20)),
            (Review: Review(userC.Id, ItemAt(tripC, 0), 5, "Chưa duyệt"), At: Now.AddMinutes(-30)),
            (Review: Review(userC.Id, ItemAt(tripC, 1), 5, "Đã ẩn"), At: Now.AddMinutes(-40)),
            (Review: Review(userC.Id, ItemAt(tripC, 2), 5, "Đã xoá"), At: Now.AddMinutes(-50)),
            (Review: Review(userC.Id, ItemAt(tripC, 3), 5, "Rất đáng đi", ReviewQuickTags.GoodValue), At: Now.AddHours(-3)),
            (Review: Review(userD.Id, ItemAt(tripD, 0), 5, null), At: Now.AddHours(-2)),
            (Review: Review(userD.Id, ItemAt(tripD, 1), 4, "Sẽ quay lại"), At: Now.AddHours(-4))
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
        return new Seed(place.Id, otherPlace.Id, newestQualifying.Id);
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

    private sealed record Seed(Guid PlaceId, Guid OtherPlaceId, Guid NewestQualifyingReviewId);
}
