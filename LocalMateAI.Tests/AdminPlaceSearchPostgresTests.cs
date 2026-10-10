using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AdminPlaceSearchPostgresTests
{
    [Fact]
    public async Task Search_MatchesNameAndAddressWithoutAccents_TreatsWildcardsLiterally_AndSkipsDeleted()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        c.Places.AddRange(
            NewPlace("Chợ Bến Thành", "Lê Lợi, Quận 1"),
            NewPlace("Quán cà phê", "12 Lê Lai, Bến Thành"),
            NewPlace("Bảo tàng", "Phó Đức Chính"),
            NewPlace("Giảm 100% hôm nay", "Test"),
            NewPlace("Giảm 1000 đồng", "Test"),
            NewPlace("Bến Thành đã xoá", "Test", DateTime.UtcNow));
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var repository = new PlaceRepository(c);

        // Mặc định sắp theo tên; khớp cả theo tên lẫn theo địa chỉ.
        Assert.Equal(["Chợ Bến Thành", "Quán cà phê"], await NamesAsync("ben thanh"));
        Assert.Equal(["Chợ Bến Thành", "Quán cà phê"], await NamesAsync("  BẾN thành "));
        Assert.Equal(["Giảm 100% hôm nay"], await NamesAsync("100%"));
        Assert.Empty(await NamesAsync("khong co"));

        async Task<IEnumerable<string>> NamesAsync(string search) =>
            (await repository.GetPagedForAdminAsync(new AdminPlaceQuery { Search = search }))
            .Items.Select(place => place.Name);
    }

    private static Place NewPlace(string name, string address, DateTime? deletedAt = null) => new()
    {
        Name = name,
        Address = address,
        Location = new Point(106.7, 10.77) { SRID = 4326 },
        Category = PlaceCategory.Cafe,
        Status = deletedAt is null ? PlaceStatus.Active : PlaceStatus.Inactive,
        DeletedAt = deletedAt
    };
}
