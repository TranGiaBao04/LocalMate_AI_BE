using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Metro;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class MetroTimetableServiceTests
{
    // 2026-09-27 (Chủ nhật) 15:00 giờ Việt Nam = 08:00 UTC.
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public async Task GetStationDeparturesAsync_MiddleStationReturnsBothDirectionsShiftedByOffset()
    {
        var result = await CreateService().GetStationDeparturesAsync(7, Monday);

        Assert.Equal(MetroStationDeparturesResultStatus.Success, result.Status);
        var response = result.Response!;
        Assert.Equal(new MetroStationRefResponse(7, "Ga 7"), response.Station);
        Assert.Collection(response.Directions,
            direction =>
            {
                Assert.Equal(MetroDirection.TowardSuoiTien, direction.Direction);
                Assert.Equal("Ga 14", direction.TowardStationName);
                Assert.Equal(new TimeOnly(5, 12), direction.Departures[0]);   // +12 phút
                Assert.Equal(new TimeOnly(22, 12), direction.Departures[^1]);
            },
            direction =>
            {
                Assert.Equal(MetroDirection.TowardBenThanh, direction.Direction);
                Assert.Equal("Ga 1", direction.TowardStationName);
                Assert.Equal(new TimeOnly(5, 14), direction.Departures[0]);   // +14 phút
            });
        Assert.Equal(new TimeOnly(5, 12), response.FirstDeparture);
        Assert.Equal(new TimeOnly(22, 14), response.LastDeparture);
        Assert.True(response.IsEstimated);
    }

    [Theory]
    [InlineData(1, MetroDirection.TowardSuoiTien)]
    [InlineData(14, MetroDirection.TowardBenThanh)]
    public async Task GetStationDeparturesAsync_TerminusOnlyHasOutgoingDirection(int stationOrder, MetroDirection expected)
    {
        var result = await CreateService().GetStationDeparturesAsync(stationOrder, Monday);

        Assert.Equal(expected, Assert.Single(result.Response!.Directions).Direction);
    }

    [Fact]
    public async Task GetStationDeparturesAsync_UsesServiceOfRequestedWeekday()
    {
        var service = CreateService();

        var monday = await service.GetStationDeparturesAsync(1, Monday);
        var sunday = await service.GetStationDeparturesAsync(1, Today);

        Assert.Equal(new TimeOnly(22, 0), monday.Response!.LastDeparture);
        Assert.Equal(new TimeOnly(23, 0), sunday.Response!.LastDeparture);
    }

    [Fact]
    public async Task GetStationDeparturesAsync_DefaultsToVietnamToday()
    {
        // 18:00 UTC ngày 27 = 01:00 ngày 28 ở Việt Nam.
        var result = await CreateService(new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero))
            .GetStationDeparturesAsync(7, null);

        Assert.Equal(Monday, result.Response!.Date);
    }

    [Fact]
    public async Task GetStationDeparturesAsync_ReturnsStationNotFound()
    {
        var result = await CreateService().GetStationDeparturesAsync(15, Monday);

        Assert.Equal(MetroStationDeparturesResultStatus.StationNotFound, result.Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task GetStationDeparturesAsync_RejectsDateOutOfRange(int daysFromToday)
    {
        var result = await CreateService().GetStationDeparturesAsync(7, Today.AddDays(daysFromToday));

        Assert.Equal(MetroStationDeparturesResultStatus.ValidationFailed, result.Status);
        Assert.True(result.ValidationErrors!.ContainsKey("date"));
    }

    [Fact]
    public async Task GetStationDeparturesAsync_AcceptsNinetyDaysAhead()
    {
        var result = await CreateService().GetStationDeparturesAsync(7, Today.AddDays(90));

        Assert.Equal(MetroStationDeparturesResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task GetStationDeparturesAsync_FlagsDateOutsideEffectivePeriod()
    {
        var timetable = MetroTimetableTestData.Valid() with { EffectiveTo = Today };

        var result = await CreateService(timetable: timetable).GetStationDeparturesAsync(7, Monday);

        Assert.Equal(MetroStationDeparturesResultStatus.Success, result.Status);
        Assert.False(result.Response!.IsWithinEffectivePeriod);
    }

    [Fact]
    public async Task GetJourneyAsync_TowardSuoiTienComputesArrivalAndTravelTime()
    {
        var result = await CreateService().GetJourneyAsync(7, 12, Monday);

        Assert.Equal(MetroJourneyResultStatus.Success, result.Status);
        var response = result.Response!;
        Assert.Equal(MetroDirection.TowardSuoiTien, response.Direction);
        Assert.Equal("Ga 14", response.TowardStationName);
        Assert.Equal(10, response.TravelMinutes);   // 22 - 12
        Assert.Equal(5, response.StopCount);
        Assert.Equal(new MetroJourneyTripResponse(new TimeOnly(5, 12), new TimeOnly(5, 22)), response.Trips[0]);
        Assert.Equal(new TimeOnly(5, 12), response.Headways[0].From);
    }

    [Fact]
    public async Task GetJourneyAsync_LowerDestinationGoesTowardBenThanh()
    {
        var result = await CreateService().GetJourneyAsync(12, 7, Monday);

        var response = result.Response!;
        Assert.Equal(MetroDirection.TowardBenThanh, response.Direction);
        Assert.Equal("Ga 1", response.TowardStationName);
        Assert.Equal(10, response.TravelMinutes);   // 14 - 4
        Assert.Equal(new MetroJourneyTripResponse(new TimeOnly(5, 4), new TimeOnly(5, 14)), response.Trips[0]);
    }

    [Fact]
    public async Task GetJourneyAsync_UsesServiceOfRequestedWeekday()
    {
        var service = CreateService();

        var monday = await service.GetJourneyAsync(1, 14, Monday);
        var sunday = await service.GetJourneyAsync(1, 14, Today);

        Assert.Equal(new TimeOnly(22, 0), monday.Response!.Trips[^1].Departure);
        Assert.Equal(new TimeOnly(23, 0), sunday.Response!.Trips[^1].Departure);
    }

    [Fact]
    public async Task GetJourneyAsync_RejectsSameStation()
    {
        var result = await CreateService().GetJourneyAsync(7, 7, Monday);

        Assert.Equal(MetroJourneyResultStatus.ValidationFailed, result.Status);
        Assert.True(result.ValidationErrors!.ContainsKey("to"));
    }

    [Fact]
    public async Task GetJourneyAsync_ReportsDateAndSameStationErrorsTogether()
    {
        var result = await CreateService().GetJourneyAsync(7, 7, Today.AddDays(-1));

        Assert.True(result.ValidationErrors!.ContainsKey("date"));
        Assert.True(result.ValidationErrors!.ContainsKey("to"));
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(7, 15)]
    public async Task GetJourneyAsync_ReturnsStationNotFound(int from, int to)
    {
        var result = await CreateService().GetJourneyAsync(from, to, Monday);

        Assert.Equal(MetroJourneyResultStatus.StationNotFound, result.Status);
    }

    private static MetroTimetableService CreateService(DateTimeOffset? utcNow = null, MetroTimetable? timetable = null) =>
        new(
            new FakeTimetableSource(timetable ?? MetroTimetableTestData.Valid()),
            new FakeMasterDataService(),
            new FixedTimeProvider(utcNow ?? UtcNow));

    private sealed class FakeTimetableSource(MetroTimetable timetable) : IMetroTimetableSource
    {
        public MetroTimetable Timetable { get; } = timetable;
    }

    private sealed class FakeMasterDataService : IMasterDataService
    {
        public Task<MasterDataResponse> GetMasterDataAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MasterDataResponse(
                Enumerable.Range(1, 14)
                    .Select(order => new MetroStationSummaryResponse(Guid.NewGuid(), $"Ga {order}", order, 0, 0))
                    .ToList(),
                [], [], [], [],
                new TripLimitsResponse(1, 24),
                [],
                []));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
