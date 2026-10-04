using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Validators.Trips;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripRequestValidatorTests
{
    // 2026-09-26 08:00 UTC = 15:00 giờ Việt Nam.
    private static readonly TripRequestValidator Validator =
        new(new FixedTimeProvider(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void CoordinatesOnly_IsValid() =>
        Assert.True(Validator.Validate(Request(10.77, 106.69)).IsValid);

    [Fact]
    public void StartStationOnly_IsValid() =>
        Assert.True(Validator.Validate(Request(startStationOrder: 3)).IsValid);

    [Fact]
    public void StartStationWithDestinationStation_IsValid() =>
        Assert.True(Validator.Validate(Request(startStationOrder: 13, destinationStationOrder: 2)).IsValid);

    [Fact]
    public void MetroTravelMode_IsAccepted() =>
        Assert.True(Validator.Validate(Request(startStationOrder: 13, destinationStationOrder: 2) with
        {
            TravelMode = TravelMode.Metro
        }).IsValid);

    [Fact]
    public void NeitherCoordinatesNorStation_FailsOnStartLatitude() =>
        Assert.Equal(["StartLatitude"], ErrorFields(Request()));

    [Theory]
    [InlineData(10.77, 106.69)]
    [InlineData(10.77, null)]
    [InlineData(null, 106.69)]
    public void StationTogetherWithAnyCoordinate_FailsOnStartStationOrder(double? latitude, double? longitude) =>
        Assert.Equal(["StartStationOrder"], ErrorFields(Request(latitude, longitude, startStationOrder: 3)));

    [Fact]
    public void LatitudeWithoutLongitude_FailsOnStartLongitude() =>
        Assert.Equal(["StartLongitude"], ErrorFields(Request(latitude: 10.77)));

    [Fact]
    public void LongitudeWithoutLatitude_FailsOnStartLatitude() =>
        Assert.Equal(["StartLatitude"], ErrorFields(Request(longitude: 106.69)));

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(-1)]
    public void StartStationOrderOutsideLine_Fails(int order) =>
        Assert.Equal(["StartStationOrder"], ErrorFields(Request(startStationOrder: order)));

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void DestinationStationOrderOutsideLine_Fails(int order) =>
        Assert.Equal(["DestinationStationOrder"], ErrorFields(Request(10.77, 106.69, destinationStationOrder: order)));

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    public void FirstAndLastStations_AreAccepted(int order) =>
        Assert.True(Validator.Validate(Request(startStationOrder: order, destinationStationOrder: order)).IsValid);

    private static string[] ErrorFields(TripRequestDto request) =>
        Validator.Validate(request).Errors.Select(error => error.PropertyName).Distinct().ToArray();

    private static TripRequestDto Request(
        double? latitude = null,
        double? longitude = null,
        int? startStationOrder = null,
        int? destinationStationOrder = null) =>
        new(latitude, longitude, 4, 0m, 500_000m, [],
            StartStationOrder: startStationOrder, DestinationStationOrder: destinationStationOrder);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
