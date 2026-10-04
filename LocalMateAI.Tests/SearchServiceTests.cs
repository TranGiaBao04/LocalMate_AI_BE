using LocalMateAI.Application.DTOs.Search;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Application.Validators.Search;

namespace LocalMateAI.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public async Task InvalidQuery_ReturnsFieldErrors_WithoutQuerying()
    {
        var repository = new FakeSearchRepository();
        var settings = new FakeSystemSettingProvider();

        var result = await Service(repository, settings).SearchAsync(new SearchQuery { Q = "a", Limit = 0 });

        Assert.Equal(SearchResultStatus.InvalidQuery, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(["Limit", "Q"], result.ValidationErrors!.Keys.Order());
        Assert.Empty(repository.Calls);
        Assert.Empty(settings.RequestedKeys);
    }

    [Fact]
    public async Task ValidQuery_SendsNormalizedKeyword_OneExtraRow_AndClusterRadiusFromSettings()
    {
        var repository = new FakeSearchRepository();
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.StationClusterRadiusMeters, 400);

        var result = await Service(repository, settings).SearchAsync(new SearchQuery { Q = "  bến   thành ", Limit = 3 });

        Assert.Equal(SearchResultStatus.Success, result.Status);
        Assert.Null(result.ValidationErrors);
        Assert.Equal("bến thành", result.Response!.Query);
        Assert.Equal(["stations", "places", "itineraries"], repository.Calls.Select(call => call.Group));
        Assert.All(repository.Calls, call =>
        {
            Assert.Equal("bến thành", call.Keyword);
            Assert.Equal(4, call.Take);
        });
        Assert.Equal([400d, 400d, null], repository.Calls.Select(call => call.Radius));
    }

    [Fact]
    public async Task EachGroup_IsCutToLimit_AndFlagsHasMoreOnlyWhenARowIsLeftOver()
    {
        var repository = new FakeSearchRepository
        {
            // limit + 1 dòng ⇒ còn kết quả.
            Stations =
            [
                new SearchStationItem(Guid.NewGuid(), 1, "Ga 1", 3),
                new SearchStationItem(Guid.NewGuid(), 2, "Ga 2", 0),
                new SearchStationItem(Guid.NewGuid(), 3, "Ga 3", 1)
            ],
            // Đúng bằng limit ⇒ hết kết quả.
            Places = [Place("Quán 1"), Place("Quán 2")]
        };

        var result = await Service(repository).SearchAsync(new SearchQuery { Q = "ga", Limit = 2 });

        var response = result.Response!;
        Assert.Equal(["Ga 1", "Ga 2"], response.Stations.Items.Select(station => station.Name));
        Assert.True(response.Stations.HasMore);
        Assert.Equal(["Quán 1", "Quán 2"], response.Places.Items.Select(place => place.Name));
        Assert.False(response.Places.HasMore);
        Assert.Empty(response.CuratedItineraries.Items);
        Assert.False(response.CuratedItineraries.HasMore);
    }

    private static SearchService Service(FakeSearchRepository repository, FakeSystemSettingProvider? settings = null) =>
        new(repository, settings ?? new FakeSystemSettingProvider(), new SearchQueryValidator());

    private static SearchPlaceItem Place(string name) =>
        new(Guid.NewGuid(), name, "Test", "Cafe", null, 0m, 0m, null, SearchMatchField.Name);

    private sealed class FakeSearchRepository : ISearchRepository
    {
        public IReadOnlyList<SearchStationItem> Stations { get; init; } = [];
        public IReadOnlyList<SearchPlaceItem> Places { get; init; } = [];
        public IReadOnlyList<SearchCuratedItineraryItem> Itineraries { get; init; } = [];
        public List<(string Group, string Keyword, int Take, double? Radius)> Calls { get; } = [];

        public Task<IReadOnlyList<SearchStationItem>> SearchStationsAsync(string keyword, int take,
            double clusterRadiusMeters, CancellationToken cancellationToken = default)
        {
            Calls.Add(("stations", keyword, take, clusterRadiusMeters));
            return Task.FromResult(Stations);
        }

        public Task<IReadOnlyList<SearchPlaceItem>> SearchPlacesAsync(string keyword, int take,
            double clusterRadiusMeters, CancellationToken cancellationToken = default)
        {
            Calls.Add(("places", keyword, take, clusterRadiusMeters));
            return Task.FromResult(Places);
        }

        public Task<IReadOnlyList<SearchCuratedItineraryItem>> SearchCuratedItinerariesAsync(string keyword, int take,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(("itineraries", keyword, take, null));
            return Task.FromResult(Itineraries);
        }
    }
}
