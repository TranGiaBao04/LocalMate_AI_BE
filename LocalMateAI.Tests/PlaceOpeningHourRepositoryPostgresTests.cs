using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class PlaceOpeningHourRepositoryPostgresTests
{
    [Fact]
    public async Task Replace_OverwritesTheWholeWeek_AndTouchesThePlace()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var place = NewPlace("Quán ốc");
        c.Places.Add(place);
        await c.SaveChangesAsync();
        var createdUpdatedAt = place.UpdatedAt;
        c.ChangeTracker.Clear();

        var repository = new PlaceOpeningHourRepository(c);
        Assert.Empty((await repository.GetByPlaceAsync(place.Id))!);

        Assert.True(await repository.ReplaceAsync(place.Id,
        [
            new PlaceOpeningHourDto(DayOfWeek.Monday, true, null, null),
            new PlaceOpeningHourDto(DayOfWeek.Tuesday, false, new TimeOnly(17, 0), new TimeOnly(2, 0))
        ]));
        Assert.True(await repository.ReplaceAsync(place.Id,
        [
            new PlaceOpeningHourDto(DayOfWeek.Friday, false, new TimeOnly(18, 0), new TimeOnly(23, 0))
        ]));

        var stored = await repository.GetByPlaceAsync(place.Id);
        Assert.Equal(
            new PlaceOpeningHourDto(DayOfWeek.Friday, false, new TimeOnly(18, 0), new TimeOnly(23, 0)),
            Assert.Single(stored!));
        Assert.True(await c.Places.Where(p => p.Id == place.Id).Select(p => p.UpdatedAt).SingleAsync() > createdUpdatedAt);

        Assert.True(await repository.ReplaceAsync(place.Id, []));
        Assert.Empty((await repository.GetByPlaceAsync(place.Id))!);
    }

    [Fact]
    public async Task GetForPlaces_GroupsRowsByPlace_AndSkipsPlacesWithoutRows()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var withHours = NewPlace("Quán có giờ");
        var withoutHours = NewPlace("Quán chưa có giờ");
        var notAsked = NewPlace("Quán không hỏi");
        c.Places.AddRange(withHours, withoutHours, notAsked);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var repository = new PlaceOpeningHourRepository(c);
        await repository.ReplaceAsync(withHours.Id, TestOpeningHours.EveryDay(8, 17));
        await repository.ReplaceAsync(notAsked.Id, TestOpeningHours.ClosedEveryDay());

        var hours = await repository.GetForPlacesAsync([withHours.Id, withoutHours.Id]);

        Assert.Equal([withHours.Id], hours.Keys);
        Assert.Equal(7, hours[withHours.Id].Count);
        Assert.Empty(await repository.GetForPlacesAsync([]));
    }

    [Fact]
    public async Task MissingOrSoftDeletedPlace_IsNotFound_AndNothingIsWritten()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var deleted = NewPlace("Quán đã xoá", DateTime.UtcNow);
        c.Places.Add(deleted);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var repository = new PlaceOpeningHourRepository(c);
        var week = new[] { new PlaceOpeningHourDto(DayOfWeek.Monday, true, null, null) };

        Assert.Null(await repository.GetByPlaceAsync(deleted.Id));
        Assert.Null(await repository.GetByPlaceAsync(Guid.NewGuid()));
        Assert.False(await repository.ReplaceAsync(deleted.Id, week));
        Assert.False(await repository.ReplaceAsync(Guid.NewGuid(), week));
        Assert.Equal(0, await c.PlaceOpeningHours.CountAsync());
    }

    [Fact]
    public async Task ConcurrentReplaces_EndWithOneCompleteWeek()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var place = NewPlace("Quán cà phê");
        c.Places.Add(place);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var first = PlaceOpeningHourRules.WeekOrder
            .Select(day => new PlaceOpeningHourDto(day, false, new TimeOnly(7, 0), new TimeOnly(22, 0)))
            .ToArray();
        var second = PlaceOpeningHourRules.WeekOrder
            .Select(day => new PlaceOpeningHourDto(day, false, new TimeOnly(9, 0), new TimeOnly(23, 0)))
            .ToArray();

        await using var firstContext = db.Context();
        await using var secondContext = db.Context();
        var results = await Task.WhenAll(
            new PlaceOpeningHourRepository(firstContext).ReplaceAsync(place.Id, first),
            new PlaceOpeningHourRepository(secondContext).ReplaceAsync(place.Id, second));

        Assert.All(results, Assert.True);

        // Không lỗi unique (PlaceId, DayOfWeek) và kết quả là trọn một trong hai tuần.
        var stored = (await new PlaceOpeningHourRepository(c).GetByPlaceAsync(place.Id))!;
        Assert.Equal(7, stored.Count);
        var winner = stored[0].OpenTime == first[0].OpenTime ? first : second;
        Assert.Equal(winner.OrderBy(day => day.DayOfWeek), stored.OrderBy(day => day.DayOfWeek));
    }

    private static Place NewPlace(string name, DateTime? deletedAt = null) => new()
    {
        Name = name,
        Address = "Test",
        Location = new Point(106.7, 10.77) { SRID = 4326 },
        Category = PlaceCategory.Food,
        Status = deletedAt is null ? PlaceStatus.Active : PlaceStatus.Inactive,
        DeletedAt = deletedAt
    };
}
