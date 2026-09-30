using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class QueryableExtensionsTests
{
    private const string ConnectionEnvironmentVariable = "LOCALMATE_TEST_CONNECTION";

    private static readonly Guid FirstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static readonly Item[] Items =
    [
        new(ThirdId, "Cafe", 2),
        new(FirstId, "Bảo tàng", 2),
        new(SecondId, "Chợ", 1)
    ];

    [Fact]
    public void ApplySort_NoSortParameters_UsesDefaultFieldAndDirection()
    {
        var ids = Sort(new PagedQuery());

        // rank tăng dần; hai dòng rank 2 xếp theo Id.
        Assert.Equal([SecondId, FirstId, ThirdId], ids);
    }

    [Fact]
    public void ApplySort_FieldAndDirectionIgnoreCase_SortsDescending()
    {
        var ids = Sort(new PagedQuery { SortBy = " NAME ", SortDirection = "DESC" });

        Assert.Equal([SecondId, ThirdId, FirstId], ids);
    }

    [Fact]
    public void ApplySort_TiedValues_OrderedByTieBreakerEvenWhenDescending()
    {
        var ids = Sort(new PagedQuery { SortBy = "rank", SortDirection = "desc" });

        Assert.Equal([FirstId, ThirdId, SecondId], ids);
    }

    [Fact]
    public void ApplySort_UnknownField_Throws()
    {
        Assert.Throws<ArgumentException>(() => Sort(new PagedQuery { SortBy = "passwordHash" }));
    }

    [Fact]
    public void ApplySort_InvalidDirection_Throws()
    {
        Assert.Throws<ArgumentException>(() => Sort(new PagedQuery { SortDirection = "up" }));
    }

    [Fact]
    public void ApplySort_DefaultFieldNotDeclared_Throws()
    {
        var map = new SortMap<Item>("missing", false, item => item.Id).Add("name", item => item.Name);

        Assert.Throws<ArgumentException>(() => Items.AsQueryable().ApplySort(new PagedQuery(), map).ToList());
    }

    [Fact]
    public void Add_DuplicateNameIgnoringCase_Throws()
    {
        var map = CreateMap();

        Assert.Throws<ArgumentException>(() => map.Add("RANK", item => item.Rank));
    }

    [Fact]
    public void FieldNames_ReturnsDeclaredFields()
    {
        Assert.Equal(["rank", "name"], CreateMap().FieldNames);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(PagedQuery.MaxPage + 1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, PagedQuery.MaxPageSize + 1)]
    public async Task ToPagedResultAsync_InvalidPaging_ThrowsBeforeQuerying(int page, int pageSize)
    {
        var query = new PagedQuery { Page = page, PageSize = pageSize };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Items.AsQueryable().ToPagedResultAsync(query));
    }

    [Fact]
    public async Task ToPagedResultAsync_Postgres_SplitsPagesWithoutGapsOrDuplicates()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var context = CreateContext(connectionString);
        var stationMap = new SortMap<MetroStation>("order", false, station => station.Id)
            .Add("order", station => station.Order)
            .Add("name", station => station.Name);
        var expectedIds = await context.MetroStations
            .OrderBy(station => station.Order)
            .ThenBy(station => station.Id)
            .Select(station => station.Id)
            .ToListAsync();
        const int pageSize = 5;
        var expectedTotalPages = (expectedIds.Count + pageSize - 1) / pageSize;

        var collectedIds = new List<Guid>();
        for (var page = 1; page <= expectedTotalPages; page++)
        {
            var query = new PagedQuery { Page = page, PageSize = pageSize };
            var result = await context.MetroStations
                .ApplySort(query, stationMap)
                .Select(station => station.Id)
                .ToPagedResultAsync(query);

            Assert.Equal(expectedIds.Count, result.TotalCount);
            Assert.Equal(expectedTotalPages, result.TotalPages);
            collectedIds.AddRange(result.Items);
        }

        Assert.Equal(expectedIds, collectedIds);

        var beyondLastPage = new PagedQuery { Page = expectedTotalPages + 1, PageSize = pageSize };
        var emptyResult = await context.MetroStations
            .ApplySort(beyondLastPage, stationMap)
            .ToPagedResultAsync(beyondLastPage);
        Assert.Empty(emptyResult.Items);
        Assert.Equal(expectedIds.Count, emptyResult.TotalCount);
    }

    private static List<Guid> Sort(PagedQuery query) =>
        Items.AsQueryable().ApplySort(query, CreateMap()).Select(item => item.Id).ToList();

    private static SortMap<Item> CreateMap() =>
        new SortMap<Item>("rank", false, item => item.Id)
            .Add("rank", item => item.Rank)
            .Add("name", item => item.Name);

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private sealed record Item(Guid Id, string Name, int Rank);
}
