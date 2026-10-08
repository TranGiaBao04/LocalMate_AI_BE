using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripOriginResolverServiceTests
{
    private static readonly MetroStationSummaryResponse BenThanh = new(Guid.NewGuid(), "Bến Thành", 1, 10.7705, 106.6967);
    private static readonly MetroStationSummaryResponse NhaHat = new(Guid.NewGuid(), "Nhà hát Thành phố", 2, 10.7747, 106.7023);
    private static readonly MetroStationSummaryResponse DaiHocQuocGia = new(Guid.NewGuid(), "Đại học Quốc gia", 13, 10.8664, 106.8013);
    private static readonly IReadOnlyList<MetroStationSummaryResponse> Stations = [BenThanh, NhaHat, DaiHocQuocGia];

    // 2026-09-26 08:00 UTC = 15:00 thứ Bảy giờ Việt Nam.
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Coordinates_InsideCityButFarFromAnyStation_AreStillWithinServiceArea()
    {
        // Bình Chánh: cách ga gần nhất 14,5 km. Trước đây bị chặn bởi cổng 12 km, nay không còn xét khoảng cách tới ga.
        var geo = new FakeGeoService(Nearest(BenThanh, 14_500));

        var origin = await Resolver(geo).ResolveAsync(Request(10.6879, 106.5938));

        Assert.True(origin!.IsWithinServiceArea);
        Assert.Equal(14_500, origin.NearestStation.DistanceMeters);
        Assert.Equal(10.6879, origin.StartLatitude);
        Assert.Equal(106.5938, origin.StartLongitude);
    }

    [Theory]
    [InlineData(TravelMode.Auto)]
    [InlineData(TravelMode.Metro)]
    public async Task Coordinates_OutsideHoChiMinhCity_AreOutOfServiceArea_ForEveryMode(TravelMode mode)
    {
        var geo = new FakeGeoService(Nearest(DaiHocQuocGia, 1_100_000));

        var origin = await Resolver(geo).ResolveAsync(Request(21.0285, 105.8542, travelMode: mode)); // Hà Nội

        Assert.False(origin!.IsWithinServiceArea);
        Assert.Equal(TripInsufficiencyReasons.OutOfServiceArea, origin.ServiceAreaFailure);
    }

    [Theory]
    [InlineData(12_000, null)]
    [InlineData(12_001, TripInsufficiencyReasons.TooFarFromStationForMetro)]
    public async Task Metro_BoardingStationMustBeWithin12KmByDefault(double distanceMeters, string? expectedFailure)
    {
        var geo = new FakeGeoService(Nearest(BenThanh, distanceMeters));

        var origin = await Resolver(geo).ResolveAsync(Request(10.6879, 106.5938, travelMode: TravelMode.Metro));

        Assert.Equal(expectedFailure, origin!.ServiceAreaFailure);
    }

    [Fact]
    public async Task Metro_DistanceLimitFollowsAdminSetting()
    {
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.MaxServiceAreaDistanceMeters, 5_000);
        var geo = new FakeGeoService(Nearest(BenThanh, 6_000));

        var origin = await Resolver(geo, settings).ResolveAsync(Request(10.80, 106.65, travelMode: TravelMode.Metro));

        Assert.Equal(TripInsufficiencyReasons.TooFarFromStationForMetro, origin!.ServiceAreaFailure);
    }

    [Fact]
    public async Task Metro_FromCoordinates_CarriesBoardingStationAndDistance()
    {
        var geo = new FakeGeoService(Nearest(DaiHocQuocGia, 1_045));

        var origin = await Resolver(geo).ResolveAsync(
            Request(10.8756, 106.7992, destinationStationOrder: 2, travelMode: TravelMode.Metro));

        var boarding = origin!.MetroBoarding!;
        Assert.Equal(13, boarding.StationOrder);
        Assert.Equal(1_045, boarding.DistanceToStationMeters);
        Assert.False(boarding.StartsAtStation);
        Assert.Same(boarding, origin.ToScheduleOrigin().Metro);
    }

    [Fact]
    public async Task Metro_FromStation_IsNeverTooFar_AndStartsAtThatStation()
    {
        var origin = await Resolver(new FakeGeoService(null)).ResolveAsync(
            Request(startStationOrder: 13, destinationStationOrder: 2, travelMode: TravelMode.Metro));

        Assert.True(origin!.IsWithinServiceArea);
        Assert.Equal(new { StationOrder = 13, Distance = 0d, AtStation = true }, new
        {
            origin.MetroBoarding!.StationOrder,
            Distance = origin.MetroBoarding.DistanceToStationMeters,
            AtStation = origin.MetroBoarding.StartsAtStation
        });
    }

    [Theory]
    [InlineData(TravelMode.Auto)]
    [InlineData(TravelMode.Walking)]
    [InlineData(TravelMode.Motorbike)]
    public async Task OtherModes_HaveNoBoardingInfo(TravelMode mode)
    {
        var origin = await Resolver(new FakeGeoService(Nearest(BenThanh, 300)))
            .ResolveAsync(Request(10.7721, 106.6980, travelMode: mode));

        Assert.Null(origin!.MetroBoarding);
        Assert.Null(origin.ToScheduleOrigin().Metro);
    }

    [Fact]
    public async Task Metro_TimetableIsForThePlannedDate_OrTodayInVietnamWhenOmitted()
    {
        var resolver = Resolver(new FakeGeoService(null));
        const int afterLastWeekdayTrain = 22 * 60 + 3; // thứ 2–5 hết tàu qua ga 13 lúc 22:02; cuối tuần chạy tới 23:02

        var wednesday = await resolver.ResolveAsync(
            Request(startStationOrder: 13, travelMode: TravelMode.Metro, plannedDate: new DateOnly(2026, 10, 7)));
        var today = await resolver.ResolveAsync(Request(startStationOrder: 13, travelMode: TravelMode.Metro));

        Assert.Null(wednesday!.MetroBoarding!.Timetable.NextRide(13, 2, afterLastWeekdayTrain));
        Assert.NotNull(today!.MetroBoarding!.Timetable.NextRide(13, 2, afterLastWeekdayTrain)); // hôm nay là thứ Bảy
    }

    [Fact]
    public async Task Coordinates_WithoutDestination_AnchorIsTheNearestStation()
    {
        var origin = await Resolver(new FakeGeoService(Nearest(BenThanh, 300))).ResolveAsync(Request(10.7721, 106.6980));

        Assert.Equal(BenThanh.Id, origin!.NearestStation.StationId);
        Assert.Same(BenThanh, origin.AnchorStation);
        Assert.Null(origin.StartStationId);
        Assert.Null(origin.DestinationStationId);
    }

    [Fact]
    public async Task DestinationStation_BecomesAnchor_WhileBoardingStationStaysNearest()
    {
        var geo = new FakeGeoService(Nearest(DaiHocQuocGia, 1_050));

        var origin = await Resolver(geo).ResolveAsync(Request(10.8756, 106.7992, destinationStationOrder: 2));

        Assert.Equal(DaiHocQuocGia.Id, origin!.NearestStation.StationId);
        Assert.Same(NhaHat, origin.AnchorStation);
        Assert.Equal(NhaHat.Id, origin.DestinationStationId);
        Assert.Null(origin.StartStationId);
    }

    [Fact]
    public async Task StartStation_UsesStationCoordinates_AndNeverLooksUpNearestStation()
    {
        var geo = new FakeGeoService(null);

        var origin = await Resolver(geo).ResolveAsync(Request(startStationOrder: 13));

        Assert.Equal(0, geo.Calls);
        Assert.True(origin!.IsWithinServiceArea);
        Assert.Equal(DaiHocQuocGia.Latitude, origin.StartLatitude);
        Assert.Equal(DaiHocQuocGia.Longitude, origin.StartLongitude);
        Assert.Equal(DaiHocQuocGia.Id, origin.NearestStation.StationId);
        Assert.Equal(0, origin.NearestStation.DistanceMeters);
        Assert.Equal(DaiHocQuocGia.Id, origin.StartStationId);
        Assert.Same(DaiHocQuocGia, origin.AnchorStation);
    }

    [Fact]
    public async Task StartStationWithDestinationStation_RecordsBoth()
    {
        var origin = await Resolver(new FakeGeoService(null))
            .ResolveAsync(Request(startStationOrder: 13, destinationStationOrder: 1));

        Assert.Equal(DaiHocQuocGia.Id, origin!.StartStationId);
        Assert.Equal(BenThanh.Id, origin.DestinationStationId);
        Assert.Same(BenThanh, origin.AnchorStation);
    }

    [Fact]
    public async Task StartStationMissingFromDatabase_ReturnsNull() =>
        Assert.Null(await Resolver(new FakeGeoService(null)).ResolveAsync(Request(startStationOrder: 7)));

    [Fact]
    public async Task DestinationStationMissingFromDatabase_ReturnsNull() =>
        Assert.Null(await Resolver(new FakeGeoService(Nearest(BenThanh, 300)))
            .ResolveAsync(Request(10.7721, 106.6980, destinationStationOrder: 7)));

    [Fact]
    public async Task NoStations_ReturnsNull() =>
        Assert.Null(await Resolver(new FakeGeoService(null)).ResolveAsync(Request(10.8, 106.65)));

    [Fact]
    public async Task RequestWithoutCoordinatesOrStation_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Resolver(new FakeGeoService(null)).ResolveAsync(Request()));

    private static TripOriginResolverService Resolver(FakeGeoService geo, FakeSystemSettingProvider? settings = null) =>
        new(geo, new StubStationRepository(Stations), new CoordinatesValidationService(),
            new StubMetroTimetableSource(), settings ?? new FakeSystemSettingProvider(), Clock);

    private static NearestStationResult Nearest(MetroStationSummaryResponse station, double distanceMeters) =>
        new(station.Id, station.Name, station.Latitude, station.Longitude, distanceMeters);

    private static TripRequestDto Request(
        double? latitude = null,
        double? longitude = null,
        int? startStationOrder = null,
        int? destinationStationOrder = null,
        TravelMode travelMode = TravelMode.Auto,
        DateOnly? plannedDate = null) =>
        new(latitude, longitude, 4, 0m, 500_000m, [], travelMode, plannedDate,
            StartStationOrder: startStationOrder, DestinationStationOrder: destinationStationOrder);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeGeoService(NearestStationResult? nearest) : IGeoService
    {
        public int Calls { get; private set; }

        public Task<NearestStationResult?> FindNearestStationAsync(double latitude, double longitude,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(nearest);
        }

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(Guid placeId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
