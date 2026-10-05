using LocalMateAI.Application.DTOs.Search;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
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
        var scorer = new FakeScorer();

        var result = await Service(repository, settings, scorer).SearchAsync(new SearchQuery { Q = "a", Limit = 0 });

        Assert.Equal(SearchResultStatus.InvalidQuery, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(["Limit", "Q"], result.ValidationErrors!.Keys.Order());
        Assert.Empty(repository.Calls);
        Assert.Empty(settings.RequestedKeys);
        Assert.Empty(scorer.Calls);
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

    [Fact]
    public async Task PlaceGroupAlreadyFull_DoesNotScoreSemantically()
    {
        var repository = new FakeSearchRepository { Places = [Place("Quán 1"), Place("Quán 2")] };
        var scorer = new FakeScorer();

        await Service(repository, scorer: scorer).SearchAsync(new SearchQuery { Q = "quán yên tĩnh", Limit = 2 });

        Assert.Empty(scorer.Calls);
    }

    [Theory]
    [InlineData("abc", 0)]
    [InlineData("abcd", 1)]
    public async Task SemanticScoring_NeedsAtLeastFourCharacters(string keyword, int expectedCalls)
    {
        var scorer = new FakeScorer();

        await Service(new FakeSearchRepository(), scorer: scorer).SearchAsync(new SearchQuery { Q = keyword });

        Assert.Equal(expectedCalls, scorer.Calls.Count);
    }

    [Fact]
    public async Task ScorerUnavailable_ReturnsTextMatchesOnly()
    {
        var repository = new FakeSearchRepository { Places = [Place("Quán 1")] };
        var settings = new FakeSystemSettingProvider();
        var scorer = new FakeScorer { Scores = null };

        var result = await Service(repository, settings, scorer).SearchAsync(new SearchQuery { Q = "quán yên tĩnh" });

        Assert.Equal(["Quán 1"], result.Response!.Places.Items.Select(place => place.Name));
        Assert.Equal(("quán yên tĩnh", SemanticPlaceScorer.SearchTimeout), Assert.Single(scorer.Calls));
        Assert.DoesNotContain("semantic", repository.Calls.Select(call => call.Group));
        Assert.DoesNotContain(SystemSettingKeys.SemanticMinSimilarityPercent, settings.RequestedKeys);
    }

    [Fact]
    public async Task SemanticMatches_FollowTextMatches_InScoreOrder_WithoutDuplicates()
    {
        var textMatch = Place("Khớp theo tên");
        var best = Place("Khớp nghĩa nhất", SearchMatchField.Semantic);
        var second = Place("Khớp nghĩa nhì", SearchMatchField.Semantic);
        var tooFar = Place("Qua sàn nhưng xa hạng 1", SearchMatchField.Semantic);
        var repository = new FakeSearchRepository
        {
            Places = [textMatch],
            SemanticPlaces = [second, tooFar, best, Place("Trùng", SearchMatchField.Semantic) with { Id = textMatch.Id }]
        };
        var scorer = new FakeScorer
        {
            Scores = new Dictionary<Guid, double>
            {
                [textMatch.Id] = 0.80,
                [second.Id] = 0.77,
                [best.Id] = 0.79,
                [tooFar.Id] = 0.70
            }
        };

        var result = await Service(repository, scorer: scorer).SearchAsync(new SearchQuery { Q = "quán yên tĩnh" });

        var places = result.Response!.Places;
        Assert.Equal(["Khớp theo tên", "Khớp nghĩa nhất", "Khớp nghĩa nhì"], places.Items.Select(place => place.Name));
        Assert.Equal(
            [SearchMatchField.Name, SearchMatchField.Semantic, SearchMatchField.Semantic],
            places.Items.Select(place => place.MatchedOn));
        Assert.False(places.HasMore);
        // Chỉ hỏi DB những địa điểm qua cả hai luật và chưa có trong kết quả so chuỗi.
        Assert.Equal([best.Id, second.Id], Assert.Single(repository.SemanticRequests));
    }

    [Fact]
    public async Task TopScoreBelowFloor_AddsNothing_AndSkipsTheDatabase()
    {
        var repository = new FakeSearchRepository();
        var scorer = new FakeScorer { Scores = new Dictionary<Guid, double> { [Guid.NewGuid()] = 0.60 } };

        var result = await Service(repository, scorer: scorer).SearchAsync(new SearchQuery { Q = "asdf qwerty" });

        Assert.Empty(result.Response!.Places.Items);
        Assert.Empty(repository.SemanticRequests);
    }

    [Fact]
    public async Task SemanticMatches_FillOnlyTheRemainingSlots_AndFlagHasMore()
    {
        var semantic = Enumerable.Range(1, 4).Select(index => Place($"Nghĩa {index}", SearchMatchField.Semantic)).ToArray();
        var repository = new FakeSearchRepository { Places = [Place("Tên 1")], SemanticPlaces = semantic };
        var scorer = new FakeScorer
        {
            Scores = semantic.Select((place, index) => (place.Id, Score: 0.80 - index * 0.001))
                .ToDictionary(pair => pair.Id, pair => pair.Score)
        };

        var result = await Service(repository, scorer: scorer).SearchAsync(new SearchQuery { Q = "quán yên tĩnh", Limit = 3 });

        var places = result.Response!.Places;
        Assert.Equal(["Tên 1", "Nghĩa 1", "Nghĩa 2"], places.Items.Select(place => place.Name));
        Assert.True(places.HasMore);
    }

    [Fact]
    public async Task HiddenPlaces_AreSkipped_AndTheNextMatchTakesTheSlot()
    {
        var hidden = Guid.NewGuid();
        var visible = Place("Còn hiển thị", SearchMatchField.Semantic);
        var repository = new FakeSearchRepository { SemanticPlaces = [visible] };
        var scorer = new FakeScorer
        {
            Scores = new Dictionary<Guid, double> { [hidden] = 0.80, [visible.Id] = 0.78 }
        };

        var result = await Service(repository, scorer: scorer).SearchAsync(new SearchQuery { Q = "quán yên tĩnh", Limit = 1 });

        Assert.Equal(["Còn hiển thị"], result.Response!.Places.Items.Select(place => place.Name));
        Assert.False(result.Response.Places.HasMore);
    }

    [Fact]
    public async Task Thresholds_AndClusterRadius_ComeFromSettings()
    {
        var close = Place("Sát hạng 1", SearchMatchField.Semantic);
        var far = Place("Xa hạng 1", SearchMatchField.Semantic);
        var repository = new FakeSearchRepository { SemanticPlaces = [close, far] };
        var settings = new FakeSystemSettingProvider()
            .Set(SystemSettingKeys.StationClusterRadiusMeters, 400)
            .Set(SystemSettingKeys.SemanticMinSimilarityPercent, 50)
            .Set(SystemSettingKeys.SemanticMaxGapFromTopPercent, 20);
        var scorer = new FakeScorer
        {
            Scores = new Dictionary<Guid, double> { [close.Id] = 0.62, [far.Id] = 0.52, [Guid.NewGuid()] = 0.41 }
        };

        var result = await Service(repository, settings, scorer).SearchAsync(new SearchQuery { Q = "quán yên tĩnh" });

        // Mặc định (sàn 66) thì không có gì; với sàn 50 và khoảng cách 20 thì lấy được hai địa điểm.
        Assert.Equal(["Sát hạng 1", "Xa hạng 1"], result.Response!.Places.Items.Select(place => place.Name));
        Assert.Equal(400d, repository.Calls.Single(call => call.Group == "semantic").Radius);
    }

    private static SearchService Service(
        FakeSearchRepository repository,
        FakeSystemSettingProvider? settings = null,
        FakeScorer? scorer = null) =>
        new(repository, settings ?? new FakeSystemSettingProvider(), new SearchQueryValidator(), scorer ?? new FakeScorer());

    private static SearchPlaceItem Place(string name, SearchMatchField matchedOn = SearchMatchField.Name) =>
        new(Guid.NewGuid(), name, "Test", "Cafe", null, 0m, 0m, null, matchedOn);

    private sealed class FakeScorer : ISemanticPlaceScorer
    {
        public IReadOnlyDictionary<Guid, double>? Scores { get; init; }
        public List<(string Text, TimeSpan Timeout)> Calls { get; } = [];

        public Task<IReadOnlyDictionary<Guid, double>?> ScoreAsync(
            string text,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((text, timeout));
            return Task.FromResult(Scores);
        }
    }

    private sealed class FakeSearchRepository : ISearchRepository
    {
        public IReadOnlyList<SearchStationItem> Stations { get; init; } = [];
        public IReadOnlyList<SearchPlaceItem> Places { get; init; } = [];
        public IReadOnlyList<SearchPlaceItem> SemanticPlaces { get; init; } = [];
        public IReadOnlyList<SearchCuratedItineraryItem> Itineraries { get; init; } = [];
        public List<(string Group, string Keyword, int Take, double? Radius)> Calls { get; } = [];
        public List<Guid[]> SemanticRequests { get; } = [];

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

        // Như repository thật: chỉ trả những địa điểm được hỏi và đang hiển thị, không theo thứ tự nào.
        public Task<IReadOnlyList<SearchPlaceItem>> GetSemanticPlacesAsync(IReadOnlyCollection<Guid> placeIds,
            double clusterRadiusMeters, CancellationToken cancellationToken = default)
        {
            Calls.Add(("semantic", string.Empty, placeIds.Count, clusterRadiusMeters));
            SemanticRequests.Add(placeIds.ToArray());
            return Task.FromResult<IReadOnlyList<SearchPlaceItem>>(
                SemanticPlaces.Where(place => placeIds.Contains(place.Id)).ToArray());
        }

        public Task<IReadOnlyList<SearchCuratedItineraryItem>> SearchCuratedItinerariesAsync(string keyword, int take,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(("itineraries", keyword, take, null));
            return Task.FromResult(Itineraries);
        }
    }
}
