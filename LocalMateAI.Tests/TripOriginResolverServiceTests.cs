using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Tests;

public sealed class TripOriginResolverServiceTests
{
    [Theory]
    [InlineData(12_000, true)]
    [InlineData(12_001, false)]
    public async Task DefaultServiceArea_Is12Km(double distanceMeters, bool expected)
    {
        var resolver = new TripOriginResolverService(new FakeGeoService(distanceMeters), new FakeSystemSettingProvider());

        var result = await resolver.ResolveAsync(10.8, 106.65);

        Assert.Equal(expected, result!.IsWithinServiceArea);
    }

    [Fact]
    public async Task ServiceArea_FollowsAdminSetting()
    {
        var settings = new FakeSystemSettingProvider()
            .Set(SystemSettingKeys.MaxServiceAreaDistanceMeters, 5_000);
        var resolver = new TripOriginResolverService(new FakeGeoService(6_000), settings);

        var result = await resolver.ResolveAsync(10.8, 106.65);

        Assert.False(result!.IsWithinServiceArea);
        Assert.Equal(6_000, result.NearestStation.DistanceMeters);
    }

    [Fact]
    public async Task NoStations_ReturnsNull()
    {
        var resolver = new TripOriginResolverService(new FakeGeoService(null), new FakeSystemSettingProvider());

        Assert.Null(await resolver.ResolveAsync(10.8, 106.65));
    }

    private sealed class FakeGeoService(double? distanceMeters) : IGeoService
    {
        public Task<NearestStationResult?> FindNearestStationAsync(double latitude, double longitude,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(distanceMeters is { } distance
                ? new NearestStationResult(Guid.NewGuid(), "Bến Thành", 10.77, 106.69, distance)
                : null);

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(Guid placeId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
