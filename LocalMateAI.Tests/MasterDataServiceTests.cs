using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Tests;

public sealed class MasterDataServiceTests
{
    [Fact]
    public async Task GetMasterDataAsync_ReturnsLabelledQuickTagsStatusesTravelModesAndLimits()
    {
        var service = new MasterDataService(new FakeStationRepository(), new MemoryCache(new MemoryCacheOptions()));

        var result = await service.GetMasterDataAsync();

        Assert.Equal(ReviewQuickTags.All, result.ReviewQuickTags.Select(tag => tag.Code));
        Assert.All(result.ReviewQuickTags, tag => Assert.NotEqual(tag.Code, tag.Label));
        Assert.Equal(["Draft", "Finalized"], result.TripStatuses);
        Assert.Equal(["Auto", "Walking", "Motorbike"], result.TravelModes);
        Assert.Equal(new TripLimitsResponse(1, 24), result.TripLimits);
    }

    [Fact]
    public async Task GetMasterDataAsync_ReturnsTimeSlotsWithRemainingHoursUntilMidnight()
    {
        var service = new MasterDataService(new FakeStationRepository(), new MemoryCache(new MemoryCacheOptions()));

        var result = await service.GetMasterDataAsync();

        Assert.Equal(["morning", "afternoon", "evening"], result.TimeSlots.Select(slot => slot.Code));
        Assert.Equal(
            [new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(18, 0)],
            result.TimeSlots.Select(slot => slot.StartTime));
        Assert.Equal([16, 11, 6], result.TimeSlots.Select(slot => slot.MaxDurationHours));
        Assert.All(result.TimeSlots, slot => Assert.False(string.IsNullOrWhiteSpace(slot.Label)));
    }

    private sealed class FakeStationRepository : IMetroStationRepository
    {
        public Task<NearestStationResult?> FindNearestAsync(
            double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult<NearestStationResult?>(null);

        public Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MetroStationSummaryResponse>>([]);
    }
}
