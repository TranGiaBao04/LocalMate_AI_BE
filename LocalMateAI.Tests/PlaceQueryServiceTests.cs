using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class PlaceQueryServiceTests
{
    private static readonly PlaceReadModel Place = new(
        Guid.NewGuid(), "Quán ốc", "Mô tả", "Địa chỉ", 10.77, 106.70, "Food", "Active", true, 50_000, 120_000, null, []);

    [Fact]
    public async Task GetPlaceById_WithOpeningHours_ReturnsTheWholeWeek()
    {
        var hours = new FakePlaceOpeningHourRepository();
        hours.Hours[Place.Id] =
        [
            new PlaceOpeningHourDto(DayOfWeek.Monday, true, null, null),
            new PlaceOpeningHourDto(DayOfWeek.Tuesday, false, new TimeOnly(17, 0), new TimeOnly(2, 0))
        ];

        var response = await Service(hours).GetPlaceByIdAsync(Place.Id);

        var openingHours = response!.OpeningHours!;
        Assert.Equal(Place.Id, openingHours.PlaceId);
        Assert.True(openingHours.HasData);
        Assert.Equal(PlaceOpeningHourRules.WeekOrder, openingHours.Days.Select(day => day.DayOfWeek));
        Assert.True(openingHours.Days[0].IsClosed);
        Assert.Equal(new TimeOnly(2, 0), openingHours.Days[1].CloseTime);
        Assert.Null(openingHours.Days[2].OpenTime); // thứ Tư chưa nhập
    }

    [Fact]
    public async Task GetPlaceById_WithoutOpeningHours_HasNoData()
    {
        var response = await Service(new FakePlaceOpeningHourRepository()).GetPlaceByIdAsync(Place.Id);

        Assert.False(response!.OpeningHours!.HasData);
        Assert.Equal(7, response.OpeningHours.Days.Count);
        Assert.All(response.OpeningHours.Days, day => Assert.Null(day.OpenTime));
    }

    [Fact]
    public async Task GetPlaceById_UnknownPlace_ReturnsNull()
    {
        Assert.Null(await Service(new FakePlaceOpeningHourRepository()).GetPlaceByIdAsync(Guid.NewGuid()));
    }

    private static PlaceQueryService Service(FakePlaceOpeningHourRepository hours) =>
        new(
            new StubPlaceRepository { ActivePlaces = [Place] },
            new FakeGeoService(),
            new FakeSystemSettingProvider(),
            new FakeReviewRepository(),
            hours);

    private sealed class FakeGeoService : IGeoService
    {
        public Task<NearestStationResult?> FindNearestStationAsync(
            double latitude, double longitude, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(
            Guid placeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<NearestStationResult?>(new NearestStationResult(Guid.NewGuid(), "Bến Thành", 10.77, 106.70, 250));
    }

    private sealed class FakeReviewRepository : IPlaceReviewRepository
    {
        public Task<PlaceReviewSummary> GetSummaryAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlaceReviewSummary(0, 0));

        public Task<OwnedItemForReviewReadModel?> GetOwnedItemAsync(
            Guid itemId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlaceReview?> GetAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(PlaceReview review, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> DeleteAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsPlaceVisibleAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<PublicPlaceReviewResponse>> GetPagedByPlaceAsync(
            Guid placeId, PlaceReviewQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
