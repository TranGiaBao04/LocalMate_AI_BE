using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AdminPlaceOpeningHoursFilterPostgresTests
{
    [Fact]
    public async Task AdminList_FlagsAndFiltersPlacesByWhetherTheyHaveOpeningHours()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var fullWeek = NewPlace("A đủ tuần", PlaceStatus.Active);
        var oneDay = NewPlace("B một thứ", PlaceStatus.Pending);
        var none = NewPlace("C chưa có", PlaceStatus.Active);
        c.Places.AddRange(fullWeek, oneDay, none);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var hours = new PlaceOpeningHourRepository(c);
        await hours.ReplaceAsync(fullWeek.Id, TestOpeningHours.EveryDay(8, 22));
        await hours.ReplaceAsync(oneDay.Id, [new PlaceOpeningHourDto(DayOfWeek.Monday, true, null, null)]);

        var repository = new PlaceRepository(c);

        var all = (await repository.GetPagedForAdminAsync(new AdminPlaceQuery())).Items;
        Assert.Equal(
            [("A đủ tuần", true), ("B một thứ", true), ("C chưa có", false)],
            all.Select(place => (place.Name, place.HasOpeningHours)));

        Assert.Equal(["A đủ tuần", "B một thứ"], await NamesAsync(new AdminPlaceQuery { HasOpeningHours = true }));
        Assert.Equal(["C chưa có"], await NamesAsync(new AdminPlaceQuery { HasOpeningHours = false }));
        Assert.Equal(
            ["A đủ tuần"],
            await NamesAsync(new AdminPlaceQuery { HasOpeningHours = true, Status = PlaceStatus.Active }));

        Assert.True((await repository.GetAdminByIdAsync(oneDay.Id))!.HasOpeningHours);
        Assert.False((await repository.GetAdminByIdAsync(none.Id))!.HasOpeningHours);
        Assert.Equal(
            [true, true, false],
            (await repository.GetAllForAdminAsync()).OrderBy(place => place.Name).Select(place => place.HasOpeningHours));

        async Task<IEnumerable<string>> NamesAsync(AdminPlaceQuery query) =>
            (await repository.GetPagedForAdminAsync(query)).Items.Select(place => place.Name);
    }

    private static Place NewPlace(string name, PlaceStatus status) => new()
    {
        Name = name,
        Address = "Test",
        Location = new Point(106.7, 10.77) { SRID = 4326 },
        Category = PlaceCategory.Cafe,
        Status = status
    };
}
