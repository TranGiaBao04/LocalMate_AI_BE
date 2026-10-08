using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Application.Validators.Trips;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripMatchingServiceTests
{
    // 2026-09-26 01:00 UTC = 08:00 giờ Việt Nam.
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 9, 26, 1, 0, 0, TimeSpan.Zero));

    private static readonly MetroStationSummaryResponse BenThanh = new(Guid.NewGuid(), "Bến Thành", 1, 10.7705, 106.6967);
    private static readonly MetroStationSummaryResponse NhaHat = new(Guid.NewGuid(), "Nhà hát Thành phố", 2, 10.7747, 106.7023);
    private static readonly MetroStationSummaryResponse DaiHocQuocGia = new(Guid.NewGuid(), "Đại học Quốc gia", 13, 10.8664, 106.8013);

    [Fact]
    public async Task Match_LoadsCandidatesAroundAnchorStation_AndReportsBothStations()
    {
        var origin = TestTripOrigins.At(boardingStation: DaiHocQuocGia, anchorStation: NhaHat,
            startLatitude: NhaHat.Latitude, startLongitude: NhaHat.Longitude);
        var fixture = new Fixture(origin, [Place("A", NhaHat, metersNorthOfAnchor: 100)]);

        var result = await fixture.Service.MatchAsync(Request());

        var response = result.Response!;
        Assert.True(response.IsSufficient);
        Assert.Equal(NhaHat.Id, fixture.Cluster.RequestedStationId);
        Assert.Equal("Đại học Quốc gia", response.StationName);
        Assert.Equal(new StationRefDto(2, "Nhà hát Thành phố"), response.AnchorStation);
        Assert.Same(origin, result.Origin);
    }

    [Fact]
    public async Task Match_NoTags_RanksPlaceNearestToAnchorFirst_EvenWhenItBelongsToNeighbourStation()
    {
        // "Gần" thuộc cụm ga Bến Thành (cách ga của nó 600 m) nhưng chỉ cách ga cột mốc 150 m.
        // "Xa" thuộc chính ga cột mốc, cách 400 m. Luật cũ (khoảng cách tới ga của chính nó) xếp "Xa" trước.
        var near = Place("Gần", BenThanh, metersNorthOfAnchor: 150, distanceFromOwnStation: 600);
        var far = Place("Xa", NhaHat, metersNorthOfAnchor: 400, distanceFromOwnStation: 400);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [far, near]);

        var response = (await fixture.Service.MatchAsync(Request())).Response!;

        Assert.Equal(["Gần", "Xa"], response.Candidates.Select(place => place.Candidate.PlaceName));
    }

    [Fact]
    public async Task Match_TagScoreStillOutranksDistanceToAnchor()
    {
        var tagId = Guid.NewGuid();
        var near = Place("Gần, không khớp tag", NhaHat, metersNorthOfAnchor: 100);
        var far = Place("Xa, khớp tag", NhaHat, metersNorthOfAnchor: 600);
        var fixture = new Fixture(
            TestTripOrigins.At(NhaHat),
            [near, far],
            new Dictionary<Guid, IReadOnlyList<Guid>> { [far.PlaceId] = [tagId], [near.PlaceId] = [Guid.NewGuid()] });

        var response = (await fixture.Service.MatchAsync(Request(tagIds: [tagId]))).Response!;

        Assert.Equal("Xa, khớp tag", response.Candidates[0].Candidate.PlaceName);
    }

    [Fact]
    public async Task Match_EqualScoreAndDistance_OrdersByPlaceIdForStableResults()
    {
        var first = Place("A", NhaHat, metersNorthOfAnchor: 200);
        var second = Place("B", NhaHat, metersNorthOfAnchor: 200);
        var expected = new[] { first, second }.OrderBy(place => place.PlaceId).Select(place => place.PlaceName);

        var forward = (await new Fixture(TestTripOrigins.At(NhaHat), [first, second]).Service.MatchAsync(Request())).Response!;
        var reversed = (await new Fixture(TestTripOrigins.At(NhaHat), [second, first]).Service.MatchAsync(Request())).Response!;

        Assert.Equal(expected, forward.Candidates.Select(place => place.Candidate.PlaceName));
        Assert.Equal(expected, reversed.Candidates.Select(place => place.Candidate.PlaceName));
    }

    [Fact]
    public async Task Match_NoCandidateWithinBudget_ReportsInsufficientCandidates()
    {
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [Place("Đắt", NhaHat, 100, cost: 900_000m)]);

        var response = (await fixture.Service.MatchAsync(Request(budgetMax: 300_000m))).Response!;

        Assert.False(response.IsSufficient);
        Assert.Equal(TripInsufficiencyReasons.InsufficientCandidates, response.InsufficiencyReason);
        Assert.Single(response.Excluded);
    }

    [Fact]
    public async Task Match_NoCandidatesAtAll_ReportsInsufficientCandidates()
    {
        var response = (await new Fixture(TestTripOrigins.At(NhaHat), []).Service.MatchAsync(Request())).Response!;

        Assert.False(response.IsSufficient);
        Assert.Equal(TripInsufficiencyReasons.InsufficientCandidates, response.InsufficiencyReason);
    }

    [Fact]
    public async Task Match_PlaceTakesLongerThanFreeTime_ReportsDurationTooShort()
    {
        // Bảo tàng cần 90 phút mà chỉ rảnh 1 giờ: có địa điểm, nhưng không chặng nào vừa.
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [Place("Bảo tàng", NhaHat, 100, category: "Culture")]);

        var response = (await fixture.Service.MatchAsync(Request(durationHours: 1))).Response!;

        Assert.False(response.IsSufficient);
        Assert.Equal(TripInsufficiencyReasons.DurationTooShort, response.InsufficiencyReason);
        Assert.Equal(0, response.EstimatedStopCount);
    }

    [Fact]
    public async Task Match_StartTooFarForFreeTime_ReportsDurationTooShort_UsingResolvedStartCoordinates()
    {
        // Xuất phát từ ga Đại học Quốc gia (request không có toạ độ), đi bộ xuống khu Nhà hát trong 2 giờ: không kịp.
        var origin = TestTripOrigins.At(boardingStation: DaiHocQuocGia, anchorStation: NhaHat,
            startStationId: DaiHocQuocGia.Id, destinationStationId: NhaHat.Id);
        var fixture = new Fixture(origin, [Place("Cà phê", NhaHat, 100)]);
        var request = new TripRequestDto(null, null, 2, 0m, 500_000m, [], TravelMode.Walking,
            StartStationOrder: 13, DestinationStationOrder: 2);

        var response = (await fixture.Service.MatchAsync(request)).Response!;

        Assert.False(response.IsSufficient);
        Assert.Equal(TripInsufficiencyReasons.DurationTooShort, response.InsufficiencyReason);
    }

    [Fact]
    public async Task Match_OutsideServiceArea_DoesNotLoadCandidates()
    {
        var fixture = new Fixture(TestTripOrigins.At(NhaHat, withinServiceArea: false), [Place("A", NhaHat, 100)]);

        var result = await fixture.Service.MatchAsync(Request());

        Assert.Equal(TripInsufficiencyReasons.OutOfServiceArea, result.Response!.InsufficiencyReason);
        Assert.Equal(new StationRefDto(2, "Nhà hát Thành phố"), result.Response.AnchorStation);
        Assert.Equal(0, fixture.Cluster.Calls);
    }

    [Fact]
    public async Task Match_MetroTooFarFromStation_ReportsThatReason_WithoutLoadingCandidates()
    {
        var origin = TestTripOrigins.At(NhaHat, serviceAreaFailure: TripInsufficiencyReasons.TooFarFromStationForMetro);
        var fixture = new Fixture(origin, [Place("A", NhaHat, 100)]);

        var response = (await fixture.Service.MatchAsync(Request())).Response!;

        Assert.False(response.IsSufficient);
        Assert.Equal(TripInsufficiencyReasons.TooFarFromStationForMetro, response.InsufficiencyReason);
        Assert.Equal(0, fixture.Cluster.Calls);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task Match_Metro_CountsGettingToStationWaitingAndRidingIntoFreeTime(int durationHours, bool fits)
    {
        // Lên tàu ở ga 13, chơi quanh ga 2: đoạn đầu mất 42' theo lịch tàu test, quán cà phê 60' ⇒ cần 102'.
        var boarding = new MetroBoarding(13, 1045, false, TestTripOrigins.MetroDay(new DateOnly(2026, 10, 10)));
        var origin = TestTripOrigins.At(boardingStation: DaiHocQuocGia, anchorStation: NhaHat,
            startLatitude: 10.8756, startLongitude: 106.7992, metroBoarding: boarding);
        var cafe = Place("Cà phê", NhaHat, 100) with { DistanceFromStationMeters = 152 };
        var fixture = new Fixture(origin, [cafe]);
        var request = new TripRequestDto(10.8756, 106.7992, durationHours, 0m, 500_000m, [], TravelMode.Metro,
            new DateOnly(2026, 10, 10), new TimeOnly(9, 0), DestinationStationOrder: 2);

        var response = (await fixture.Service.MatchAsync(request)).Response!;

        Assert.Equal(fits, response.IsSufficient);
        Assert.Equal(fits ? null : TripInsufficiencyReasons.DurationTooShort, response.InsufficiencyReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Match_WithoutNote_DoesNotScoreSemantically(string? note)
    {
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [Place("A", NhaHat, 100)]);

        var response = (await fixture.Service.MatchAsync(Request(note: note))).Response!;

        Assert.False(response.NoteApplied);
        Assert.Empty(fixture.Scorer.Calls);
    }

    [Fact]
    public async Task Match_NoteLiftsMatchingPlaceAboveNearerPlace_WithoutChangingMatchScore()
    {
        var near = Place("Gần", NhaHat, metersNorthOfAnchor: 100);
        var matchesNote = Place("Xa, hợp ghi chú", NhaHat, metersNorthOfAnchor: 600);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [near, matchesNote]);
        fixture.Scorer.Scores = new Dictionary<Guid, double> { [near.PlaceId] = 0.60, [matchesNote.PlaceId] = 0.72 };

        var withoutNote = (await fixture.Service.MatchAsync(Request())).Response!;
        var withNote = (await fixture.Service.MatchAsync(Request(note: "  muốn chỗ   yên tĩnh "))).Response!;

        Assert.Equal(["Gần", "Xa, hợp ghi chú"], withoutNote.Candidates.Select(place => place.Candidate.PlaceName));
        Assert.Equal(["Xa, hợp ghi chú", "Gần"], withNote.Candidates.Select(place => place.Candidate.PlaceName));
        Assert.True(withNote.NoteApplied);
        Assert.False(withoutNote.NoteApplied);
        // MatchScore trả ra vẫn là điểm tag thuần (không tag ⇒ mọi địa điểm bằng nhau).
        Assert.Single(withNote.Candidates.Select(place => place.MatchScore).Distinct());
        Assert.Equal(("muốn chỗ yên tĩnh", SemanticPlaceScorer.PlanningTimeout), Assert.Single(fixture.Scorer.Calls));
    }

    [Fact]
    public async Task Match_NoCandidateReachesTheFloor_NoteIsNotApplied_AndRankingIsUnchanged()
    {
        var near = Place("Gần", NhaHat, metersNorthOfAnchor: 100);
        var far = Place("Xa", NhaHat, metersNorthOfAnchor: 600);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [near, far]);
        fixture.Scorer.Scores = new Dictionary<Guid, double> { [near.PlaceId] = 0.58, [far.PlaceId] = 0.65 };

        var response = (await fixture.Service.MatchAsync(Request(note: "sửa xe máy ở đâu"))).Response!;

        Assert.False(response.NoteApplied);
        Assert.Equal(["Gần", "Xa"], response.Candidates.Select(place => place.Candidate.PlaceName));
    }

    [Fact]
    public async Task Match_ScorerUnavailable_NoteIsNotApplied_AndRankingIsUnchanged()
    {
        var near = Place("Gần", NhaHat, metersNorthOfAnchor: 100);
        var far = Place("Xa", NhaHat, metersNorthOfAnchor: 600);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [far, near]);
        fixture.Scorer.Scores = null;

        var response = (await fixture.Service.MatchAsync(Request(note: "muốn chỗ yên tĩnh"))).Response!;

        Assert.False(response.NoteApplied);
        Assert.Equal(["Gần", "Xa"], response.Candidates.Select(place => place.Candidate.PlaceName));
        Assert.Single(fixture.Scorer.Calls);
    }

    [Fact]
    public async Task Match_NoteWeightZero_SkipsTheScorer()
    {
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [Place("A", NhaHat, 100)]);
        fixture.Settings.Set(SystemSettingKeys.NoteWeightPercent, 0);

        var response = (await fixture.Service.MatchAsync(Request(note: "muốn chỗ yên tĩnh"))).Response!;

        Assert.False(response.NoteApplied);
        Assert.Empty(fixture.Scorer.Calls);
    }

    [Theory]
    [InlineData(20, "Khớp tag")]
    [InlineData(80, "Hợp ghi chú")]
    public async Task Match_NoteWeight_DecidesBetweenTagMatchAndNoteMatch(int weightPercent, string expectedFirst)
    {
        var tagId = Guid.NewGuid();
        var matchesTag = Place("Khớp tag", NhaHat, metersNorthOfAnchor: 300);
        var matchesNote = Place("Hợp ghi chú", NhaHat, metersNorthOfAnchor: 300);
        var fixture = new Fixture(
            TestTripOrigins.At(NhaHat),
            [matchesTag, matchesNote],
            new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [matchesTag.PlaceId] = [tagId],
                [matchesNote.PlaceId] = [Guid.NewGuid()]
            });
        fixture.Settings.Set(SystemSettingKeys.NoteWeightPercent, weightPercent);
        // Vượt sàn 66 đủ 10 điểm ⇒ điểm ghi chú tối đa.
        fixture.Scorer.Scores = new Dictionary<Guid, double> { [matchesNote.PlaceId] = 0.80 };

        var response = (await fixture.Service.MatchAsync(Request(tagIds: [tagId], note: "muốn chỗ yên tĩnh"))).Response!;

        Assert.True(response.NoteApplied);
        Assert.Equal(expectedFirst, response.Candidates[0].Candidate.PlaceName);
    }

    [Fact]
    public async Task Match_NoteNeverBringsBackPlacesExcludedByBudget()
    {
        var affordable = Place("Vừa túi tiền", NhaHat, metersNorthOfAnchor: 100);
        var expensive = Place("Đắt nhưng hợp ghi chú", NhaHat, metersNorthOfAnchor: 100, cost: 900_000m);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [affordable, expensive]);
        fixture.Scorer.Scores = new Dictionary<Guid, double> { [expensive.PlaceId] = 0.90, [affordable.PlaceId] = 0.50 };

        var response = (await fixture.Service.MatchAsync(Request(budgetMax: 300_000m, note: "nhà hàng sang trọng"))).Response!;

        Assert.Equal(["Vừa túi tiền"], response.Candidates.Select(place => place.Candidate.PlaceName));
        Assert.Single(response.Excluded);
        // Địa điểm hợp ghi chú đã bị loại vì ngân sách, các ứng viên còn lại đều dưới sàn.
        Assert.False(response.NoteApplied);
    }

    [Fact]
    public async Task Match_MatchesNote_IsSetOnlyForCandidatesCloseToTheBestMatch()
    {
        var best = Place("Khớp nhất", NhaHat, metersNorthOfAnchor: 100);
        var close = Place("Sát hạng 1", NhaHat, metersNorthOfAnchor: 200);
        var aboveFloorButFar = Place("Qua sàn nhưng xa hạng 1", NhaHat, metersNorthOfAnchor: 300);
        var belowFloor = Place("Dưới sàn", NhaHat, metersNorthOfAnchor: 400);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [best, close, aboveFloorButFar, belowFloor]);
        fixture.Scorer.Scores = new Dictionary<Guid, double>
        {
            [best.PlaceId] = 0.786,
            [close.PlaceId] = 0.750,
            [aboveFloorButFar.PlaceId] = 0.705,
            [belowFloor.PlaceId] = 0.60,
            // Địa điểm khớp hơn nữa nhưng không nằm trong tập ứng viên: không được kéo mốc "khớp nhất" lên.
            [Guid.NewGuid()] = 0.95
        };

        var response = (await fixture.Service.MatchAsync(Request(durationHours: 8, note: "hóng gió ngắm sông"))).Response!;

        Assert.Equal(
            ["Khớp nhất", "Sát hạng 1"],
            response.Candidates.Where(place => place.MatchesNote).Select(place => place.Candidate.PlaceName));
        Assert.Equal(4, response.Candidates.Count);
    }

    [Fact]
    public async Task Match_NoteNotApplied_LeavesMatchesNoteFalse()
    {
        var place = Place("A", NhaHat, metersNorthOfAnchor: 100);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [place]);
        fixture.Scorer.Scores = new Dictionary<Guid, double> { [place.PlaceId] = 0.60 };

        var response = (await fixture.Service.MatchAsync(Request(note: "sửa xe máy ở đâu"))).Response!;

        Assert.False(Assert.Single(response.Candidates).MatchesNote);
    }

    [Fact]
    public async Task Match_FloorForNote_ComesFromSettings()
    {
        var near = Place("Gần", NhaHat, metersNorthOfAnchor: 100);
        var far = Place("Xa", NhaHat, metersNorthOfAnchor: 600);
        var fixture = new Fixture(TestTripOrigins.At(NhaHat), [near, far]);
        fixture.Settings.Set(SystemSettingKeys.SemanticMinSimilarityPercent, 50);
        fixture.Scorer.Scores = new Dictionary<Guid, double> { [near.PlaceId] = 0.40, [far.PlaceId] = 0.60 };

        var response = (await fixture.Service.MatchAsync(Request(note: "muốn chỗ yên tĩnh"))).Response!;

        Assert.True(response.NoteApplied);
        Assert.Equal("Xa", response.Candidates[0].Candidate.PlaceName);
    }

    private static TripRequestDto Request(
        int durationHours = 4, decimal budgetMax = 500_000m, IReadOnlyList<Guid>? tagIds = null, string? note = null) =>
        new(NhaHat.Latitude, NhaHat.Longitude, durationHours, 0m, budgetMax, tagIds ?? [], Note: note);

    // 0,001 độ vĩ ≈ 111 m: đặt địa điểm thẳng hướng bắc của ga Nhà hát để khoảng cách tới ga cột mốc dễ kiểm soát.
    private static PlaceCandidateDto Place(
        string name,
        MetroStationSummaryResponse ownStation,
        double metersNorthOfAnchor,
        double? distanceFromOwnStation = null,
        string category = "Cafe",
        decimal cost = 50_000m) =>
        new(Guid.NewGuid(), name, "Địa chỉ", NhaHat.Latitude + metersNorthOfAnchor / 111_000, NhaHat.Longitude,
            category, 0m, cost, null, ownStation.Id, ownStation.Name, ownStation.Order,
            distanceFromOwnStation ?? metersNorthOfAnchor);

    private sealed class Fixture
    {
        public Fixture(
            TripOriginResolution origin,
            IReadOnlyList<PlaceCandidateDto> candidates,
            IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? placeTagIds = null)
        {
            Cluster = new FakeCluster(candidates);
            Service = new TripMatchingService(
                new TripRequestValidator(Clock),
                new TripCriteriaNormalizationService(),
                new FakeOrigin(origin),
                Cluster,
                new CandidateFilterService(),
                new TagSimilarityScorer(),
                new StubPlaceRepository
                {
                    PlaceTagIds = placeTagIds ?? new Dictionary<Guid, IReadOnlyList<Guid>>()
                },
                Settings,
                Scorer);
        }

        public TripMatchingService Service { get; }
        public FakeCluster Cluster { get; }
        public FakeSystemSettingProvider Settings { get; } = new();
        public FakeSemanticPlaceScorer Scorer { get; } = new();
    }

    private sealed class FakeCluster(IReadOnlyList<PlaceCandidateDto> candidates) : IMetroClusterMatchingService
    {
        public int Calls { get; private set; }
        public Guid? RequestedStationId { get; private set; }

        public Task<IReadOnlyList<PlaceCandidateDto>> GetCandidatesAsync(
            Guid originStationId, CancellationToken cancellationToken = default)
        {
            Calls++;
            RequestedStationId = originStationId;
            return Task.FromResult(candidates);
        }
    }

    private sealed class FakeOrigin(TripOriginResolution origin) : ITripOriginResolverService
    {
        public Task<TripOriginResolution?> ResolveAsync(
            TripRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult<TripOriginResolution?>(origin);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
