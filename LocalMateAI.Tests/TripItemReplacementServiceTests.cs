using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class TripItemReplacementServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid CurrentPlaceId = Guid.NewGuid();
    private static readonly Guid NewPlaceId = Guid.NewGuid();
    private static readonly Guid StationA = Guid.NewGuid();
    private static readonly Guid StationB = Guid.NewGuid();

    [Fact]
    public async Task ReplaceAsync_EmptyTripId_ReturnsInvalidId()
    {
        var (sut, _) = Create();

        var result = await sut.ReplaceAsync(UserId, Guid.Empty, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.InvalidId, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_EmptyItemId_ReturnsInvalidId()
    {
        var (sut, _) = Create();

        var result = await sut.ReplaceAsync(UserId, TripId, Guid.Empty, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.InvalidId, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_EmptyNewPlaceId_ReturnsInvalidPlaceId()
    {
        var (sut, _) = Create();

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, Guid.Empty);

        Assert.Equal(ReplaceItineraryItemResultStatus.InvalidPlaceId, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_UserNotPersisted_ReturnsUserNotFound()
    {
        var (sut, _) = Create(userId: Guid.NewGuid());

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.UserNotFound, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_ItemNotOwned_ReturnsItemNotFound()
    {
        var (sut, _) = Create(contexts: [null]);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.ItemNotFound, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_FinalizedTrip_ReturnsTripFinalized()
    {
        var (sut, _) = Create(contexts: [MakeContext(TripStatus.Finalized)]);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.TripFinalized, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_SamePlaceAsCurrent_ReturnsSamePlace()
    {
        var (sut, _) = Create();

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, CurrentPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.SamePlace, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_PlaceAlreadyInTrip_ReturnsPlaceAlreadyInTrip()
    {
        var (sut, _) = Create(contexts: [MakeContext(TripStatus.Draft, CurrentPlaceId, NewPlaceId)]);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.PlaceAlreadyInTrip, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_NewPlaceNotActive_ReturnsPlaceNotFound()
    {
        var (sut, _) = Create(newPlace: null, hasNewPlace: false);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.PlaceNotFound, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_SameStationAndCost_ReplacesWithoutWarnings()
    {
        var (sut, items) = Create();

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.Success, result.Status);
        Assert.Empty(result.Response!.Warnings);
        Assert.Equal(NewPlaceId, result.Response.Place.Id);
        Assert.Equal(NewPlaceId, items.ReplacedPlaceId);
    }

    [Fact]
    public async Task ReplaceAsync_UsesNewPlaceCostMaxAsBudget()
    {
        var (sut, items) = Create(newPlace: MakeNewPlace(costMax: 80));

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(80, items.ReplacedBudget);
        Assert.Equal(80, result.Response!.EstimatedBudget);
    }

    [Fact]
    public async Task ReplaceAsync_DifferentStation_WarnsDifferentStation()
    {
        var (sut, _) = Create(newStation: StationB);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(
            new[] { TripItemReplacementService.DifferentStationWarning },
            result.Response!.Warnings.ToArray());
    }

    [Fact]
    public async Task ReplaceAsync_MoreThanToleranceAboveCurrentCost_WarnsHigherCost()
    {
        var (sut, _) = Create(newPlace: MakeNewPlace(costMax: 126), currentCostMax: 100);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(
            new[] { TripItemReplacementService.HigherCostWarning },
            result.Response!.Warnings.ToArray());
    }

    [Fact]
    public async Task ReplaceAsync_HigherPercentSetting_NoHigherCostWarning()
    {
        var settings = new FakeSystemSettingProvider()
            .Set(SystemSettingKeys.AlternativeMaxCostIncreasePercent, 50);
        var (sut, _) = Create(newPlace: MakeNewPlace(costMax: 140), currentCostMax: 100, settings: settings);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Empty(result.Response!.Warnings);
        Assert.Contains(SystemSettingKeys.AlternativeMaxCostIncreasePercent, settings.RequestedKeys);
    }

    [Fact]
    public async Task ReplaceAsync_DifferentStationAndHigherCost_ReturnsBothWarnings()
    {
        var (sut, _) = Create(
            newPlace: MakeNewPlace(costMax: 300),
            currentCostMax: 100,
            newStation: StationB);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(
            new[]
            {
                TripItemReplacementService.DifferentStationWarning,
                TripItemReplacementService.HigherCostWarning
            },
            result.Response!.Warnings.ToArray());
    }

    [Fact]
    public async Task ReplaceAsync_TripFinalizedDuringUpdate_ReturnsTripFinalized()
    {
        var (sut, _) = Create(
            contexts: [MakeContext(TripStatus.Draft), MakeContext(TripStatus.Finalized)],
            replaceSucceeds: false);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.TripFinalized, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_PlaceAddedDuringUpdate_ReturnsPlaceAlreadyInTrip()
    {
        var (sut, _) = Create(
            contexts:
            [
                MakeContext(TripStatus.Draft),
                MakeContext(TripStatus.Draft, CurrentPlaceId, NewPlaceId)
            ],
            replaceSucceeds: false);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.PlaceAlreadyInTrip, result.Status);
    }

    [Fact]
    public async Task ReplaceAsync_RecalculatesTimesOfStopsAfterTheReplacedOne()
    {
        // Địa điểm mới cách chặng kế 2,89 km: Tự động ⇒ xe máy 8'. Chặng kế dời từ 09:31 sang 08:00 + 90' + 8' = 09:38.
        var next = new TimelineItemSnapshot(Guid.NewGuid(), 1, new TimeOnly(9, 31), 90, 10.79, 106.69);
        var (sut, items) = Create(snapshots: [FirstStop(new TimeOnly(8, 0)), next]);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.Success, result.Status);
        Assert.Equal([new TimelineItemUpdate(next.ItemId, 1, new TimeOnly(9, 38))], items.RecalculatedUpdates);
        Assert.Same(items.Timeline, result.Response!.Items);
    }

    [Theory]
    [InlineData(3, true)]  // rời 07:50, rảnh 3 giờ ⇒ phải xong trước 10:50, lịch mới xong 11:08
    [InlineData(4, false)] // rảnh 4 giờ ⇒ tới 11:50, vẫn vừa
    public async Task ReplaceAsync_TimelineLongerThanFreeTime_WarnsButStillReplaces(int durationHours, bool warns)
    {
        var next = new TimelineItemSnapshot(Guid.NewGuid(), 1, new TimeOnly(9, 31), 90, 10.79, 106.69);
        var context = MakeContext(TripStatus.Draft) with
        {
            TripPlannedStartAt = new DateTime(2026, 10, 10, 7, 50, 0),
            TripDurationHours = durationHours
        };
        var (sut, items) = Create(contexts: [context], snapshots: [FirstStop(new TimeOnly(8, 0)), next]);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.Success, result.Status);
        Assert.Equal(NewPlaceId, items.ReplacedPlaceId);
        Assert.Equal(warns, result.Response!.Warnings.Contains(TripItemReplacementService.ExceedsDurationWarning));
    }

    [Fact]
    public async Task ReplaceAsync_TimelineWouldCrossMidnight_IsRejectedWithoutReplacing()
    {
        // 22:00 + 90' + 8' đi + 90' = 01:08 hôm sau: giờ lưu kiểu TimeOnly sẽ quay vòng nên không cho thay.
        var next = new TimelineItemSnapshot(Guid.NewGuid(), 1, new TimeOnly(23, 31), 90, 10.79, 106.69);
        var (sut, items) = Create(snapshots: [FirstStop(new TimeOnly(22, 0)), next]);

        var result = await sut.ReplaceAsync(UserId, TripId, ItemId, NewPlaceId);

        Assert.Equal(ReplaceItineraryItemResultStatus.CrossesMidnight, result.Status);
        Assert.Null(items.ReplacedPlaceId);
    }

    private static TimelineItemSnapshot FirstStop(TimeOnly time) => new(ItemId, 0, time, 90, 10.77, 106.69);

    private static (TripItemReplacementService Sut, FakeItineraryItemRepository Items) Create(
        Guid? userId = null,
        IReadOnlyList<OwnedItemAlternativesReadModel?>? contexts = null,
        PlaceReadModel? newPlace = null,
        bool hasNewPlace = true,
        decimal currentCostMax = 100,
        Guid? newStation = null,
        bool replaceSucceeds = true,
        FakeSystemSettingProvider? settings = null,
        IReadOnlyList<TimelineItemSnapshot>? snapshots = null)
    {
        var items = new FakeItineraryItemRepository(
            contexts ?? [MakeContext(TripStatus.Draft)],
            replaceSucceeds,
            snapshots ?? [FirstStop(new TimeOnly(8, 0))]);
        var resolvedNewPlace = hasNewPlace ? newPlace ?? MakeNewPlace(costMax: 100) : null;
        var sut = new TripItemReplacementService(
            new FakeUserRepository(userId ?? UserId),
            items,
            new FakePlaceRepository(resolvedNewPlace, currentCostMax),
            new FakeGeoService(newStation ?? StationA),
            settings ?? new FakeSystemSettingProvider(),
            new ItineraryTimelineRecalculator(),
            new StubMetroTimetableSource());

        return (sut, items);
    }

    private static OwnedItemAlternativesReadModel MakeContext(
        TripStatus status,
        params Guid[] tripPlaceIds) =>
        new(ItemId, TripId, status, CurrentPlaceId, tripPlaceIds.Length == 0 ? [CurrentPlaceId] : tripPlaceIds);

    private static PlaceReadModel MakeNewPlace(decimal costMax) =>
        new(NewPlaceId, "New place", null, "Address", 10.77, 106.69, "Food", "Active", false, 0, costMax, null, []);

    private sealed class FakeUserRepository(Guid userId) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid requestedUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == requestedUserId ? new User { Id = requestedUserId } : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdForUpdateAsync(Guid requestedUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeItineraryItemRepository(
        IReadOnlyList<OwnedItemAlternativesReadModel?> contexts,
        bool replaceSucceeds,
        IReadOnlyList<TimelineItemSnapshot> snapshots) : IItineraryItemRepository
    {
        private int _reads;

        public decimal? ReplacedBudget { get; private set; }

        public Guid? ReplacedPlaceId { get; private set; }

        public IReadOnlyList<TimelineItemUpdate>? RecalculatedUpdates { get; private set; }

        public IReadOnlyList<ItineraryTimelineItemResponse> Timeline { get; } = [];

        public Task<OwnedItemAlternativesReadModel?> GetOwnedItemForAlternativesAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(contexts[Math.Min(_reads++, contexts.Count - 1)]);

        public Task<ReplaceItemPersistenceResult> ReplaceItemPlaceAndRecalculateTimelineAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            Guid newPlaceId,
            decimal newEstimatedBudget,
            Func<TimelineRecalculationInput, IReadOnlyList<TimelineItemUpdate>?> recalculateTimeline,
            CancellationToken cancellationToken = default)
        {
            if (!replaceSucceeds)
            {
                return Task.FromResult(new ReplaceItemPersistenceResult(ReplaceItemPersistenceStatus.NotEligible));
            }

            // Như repository thật: đưa các chặng sau khi thay cho bộ tính lại giờ; null nghĩa là bị từ chối, không ghi gì.
            RecalculatedUpdates = recalculateTimeline(new TimelineRecalculationInput(
                snapshots, snapshots[0].ScheduledTime, TravelMode.Auto));
            if (RecalculatedUpdates is null)
            {
                return Task.FromResult(new ReplaceItemPersistenceResult(ReplaceItemPersistenceStatus.CrossesMidnight));
            }

            ReplacedPlaceId = newPlaceId;
            ReplacedBudget = newEstimatedBudget;
            return Task.FromResult(new ReplaceItemPersistenceResult(
                ReplaceItemPersistenceStatus.Replaced,
                new ReplacedItemReadModel(itemId, tripId, 0, new TimeOnly(8, 0), 90, newEstimatedBudget),
                Timeline));
        }

        public Task<DeleteItineraryItemPersistenceResult> DeleteItemAndRecalculateTimelineAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            Func<TimelineRecalculationInput, IReadOnlyList<TimelineItemUpdate>> recalculateTimeline,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGeoService(Guid newPlaceStation) : IGeoService
    {
        public Task<NearestStationResult?> FindNearestStationAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(
            Guid placeId,
            CancellationToken cancellationToken = default)
        {
            var stationId = placeId == NewPlaceId ? newPlaceStation : StationA;
            return Task.FromResult<NearestStationResult?>(
                new NearestStationResult(stationId, "Station", 10.77, 106.69, 100));
        }
    }

    private sealed class FakePlaceRepository(PlaceReadModel? newPlace, decimal currentCostMax) : IPlaceRepository
    {
        public Task<PlaceReadModel?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == NewPlaceId ? newPlace : null);

        public Task<Place?> GetByIdAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Place?>(placeId == CurrentPlaceId
                ? new Place
                {
                    Id = CurrentPlaceId,
                    Name = "Current place",
                    Address = "Address",
                    Location = new Point(106.69, 10.77),
                    EstimatedCostMax = currentCostMax
                }
                : null);

        public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(
            double radiusMeters,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Point?> GetLocationAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(
            double latitude,
            double longitude,
            double radiusMeters,
            PlaceCategory? category,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(
            IReadOnlyList<Guid> placeIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<AdminPlaceResponse>> GetPagedForAdminAsync(
            AdminPlaceQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminPlaceResponse?> GetAdminByIdAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Place place, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeletePlacePersistenceResult> DeleteForAdminAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
