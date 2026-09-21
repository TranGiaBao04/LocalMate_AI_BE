using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class TripAlternativesServiceTests
{
    private static readonly Guid StationA = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public async Task GetAlternativesAsync_EmptyTripId_ReturnsInvalidId()
    {
        var result = await CreateSut().GetAlternativesAsync(UserId, Guid.Empty, ItemId, null);

        Assert.Equal(TripAlternativesResultStatus.InvalidId, result.Status);
    }

    [Fact]
    public async Task GetAlternativesAsync_EmptyItemId_ReturnsInvalidId()
    {
        var result = await CreateSut().GetAlternativesAsync(UserId, TripId, Guid.Empty, null);

        Assert.Equal(TripAlternativesResultStatus.InvalidId, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task GetAlternativesAsync_LimitOutOfRange_ReturnsInvalidLimit(int limit)
    {
        var result = await CreateSut().GetAlternativesAsync(UserId, TripId, ItemId, limit);

        Assert.Equal(TripAlternativesResultStatus.InvalidLimit, result.Status);
    }

    [Fact]
    public async Task GetAlternativesAsync_UserNotPersisted_ReturnsUserNotFound()
    {
        var result = await CreateSut(userId: null).GetAlternativesAsync(UserId, TripId, ItemId, null);

        Assert.Equal(TripAlternativesResultStatus.UserNotFound, result.Status);
    }

    [Fact]
    public async Task GetAlternativesAsync_ItemNotOwned_ReturnsItemNotFound()
    {
        var result = await CreateSut(userId: UserId, item: null)
            .GetAlternativesAsync(UserId, TripId, ItemId, null);

        Assert.Equal(TripAlternativesResultStatus.ItemNotFound, result.Status);
    }

    [Fact]
    public async Task GetAlternativesAsync_FinalizedTrip_ReturnsTripFinalized()
    {
        var current = MakeCandidate(distance: 50);
        var item = MakeItem(current.PlaceId, TripStatus.Finalized);

        var result = await CreateSut(UserId, item, Station(), [current])
            .GetAlternativesAsync(UserId, TripId, ItemId, null);

        Assert.Equal(TripAlternativesResultStatus.TripFinalized, result.Status);
    }

    [Fact]
    public async Task GetAlternativesAsync_NoStationInDatabase_Throws()
    {
        var item = MakeItem(Guid.NewGuid());

        var sut = CreateSut(UserId, item, station: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.GetAlternativesAsync(UserId, TripId, ItemId, null));
    }

    [Fact]
    public async Task GetAlternativesAsync_CurrentPlaceOutsideCluster_ReturnsEmptySuccess()
    {
        var item = MakeItem(Guid.NewGuid());
        var other = MakeCandidate(distance: 100);

        var result = await CreateSut(UserId, item, Station(), [other])
            .GetAlternativesAsync(UserId, TripId, ItemId, null);

        Assert.Equal(TripAlternativesResultStatus.Success, result.Status);
        Assert.Empty(result.Alternatives!);
    }

    [Fact]
    public async Task GetAlternativesAsync_ExcludesPlacesAlreadyInTripAndReturnsRanked()
    {
        var current = MakeCandidate(distance: 50);
        var near = MakeCandidate(distance: 100);
        var far = MakeCandidate(distance: 200);
        var inTrip = MakeCandidate(distance: 60);
        var item = MakeItem(current.PlaceId, TripStatus.Draft, current.PlaceId, inTrip.PlaceId);

        var result = await CreateSut(UserId, item, Station(), [current, far, inTrip, near])
            .GetAlternativesAsync(UserId, TripId, ItemId, null);

        Assert.Equal(TripAlternativesResultStatus.Success, result.Status);
        Assert.Equal(
            new[] { near.PlaceId, far.PlaceId },
            result.Alternatives!.Select(alternative => alternative.PlaceId).ToArray());
    }

    [Fact]
    public async Task GetAlternativesAsync_NoLimit_UsesDefaultOfFive()
    {
        var current = MakeCandidate(distance: 50);
        var candidates = Enumerable.Range(1, 8)
            .Select(index => MakeCandidate(distance: 100 + index))
            .Append(current)
            .ToList();
        var item = MakeItem(current.PlaceId);

        var result = await CreateSut(UserId, item, Station(), candidates)
            .GetAlternativesAsync(UserId, TripId, ItemId, null);

        Assert.Equal(TripAlternativesService.DefaultLimit, result.Alternatives!.Count);
    }

    private static TripAlternativesService CreateSut(
        Guid? userId = null,
        OwnedItemAlternativesReadModel? item = null,
        NearestStationResult? station = null,
        IReadOnlyList<PlaceCandidateDto>? candidates = null) =>
        new(
            new FakeUserRepository(userId),
            new FakeItineraryItemRepository(item),
            new FakeGeoService(station),
            new FakeClusterMatchingService(candidates ?? []),
            new FakePlaceRepository(),
            new AlternativePlaceFinder(new TagSimilarityScorer()));

    private static NearestStationResult Station() =>
        new(StationA, "Bến Thành", 10.77, 106.69, 100);

    private static OwnedItemAlternativesReadModel MakeItem(
        Guid placeId,
        TripStatus status = TripStatus.Draft,
        params Guid[] tripPlaceIds) =>
        new(ItemId, TripId, status, placeId, tripPlaceIds);

    private static PlaceCandidateDto MakeCandidate(double distance) =>
        new(
            Guid.NewGuid(),
            "Place",
            "Address",
            10.77,
            106.69,
            "Food",
            0,
            100,
            null,
            StationA,
            "Bến Thành",
            1,
            distance);

    private sealed class FakeUserRepository(Guid? userId) : IUserRepository
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

    private sealed class FakeItineraryItemRepository(OwnedItemAlternativesReadModel? item) : IItineraryItemRepository
    {
        public Task<OwnedItemAlternativesReadModel?> GetOwnedItemForAlternativesAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(item);

        public Task<ReplacedItemReadModel?> ReplaceItemPlaceIfEligibleAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            Guid newPlaceId,
            decimal newEstimatedBudget,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeleteItineraryItemPersistenceResult> DeleteItemAndRecalculateTimelineAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            Func<IReadOnlyList<TimelineItemSnapshot>, IReadOnlyList<TimelineItemUpdate>> recalculateTimeline,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGeoService(NearestStationResult? station) : IGeoService
    {
        public Task<NearestStationResult?> FindNearestStationAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(station);
    }

    private sealed class FakeClusterMatchingService(IReadOnlyList<PlaceCandidateDto> candidates)
        : IMetroClusterMatchingService
    {
        public Task<IReadOnlyList<PlaceCandidateDto>> GetCandidatesAsync(
            Guid originStationId,
            double radiusMeters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }

    private sealed class FakePlaceRepository : IPlaceRepository
    {
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(
            IReadOnlyList<Guid> placeIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(
                new Dictionary<Guid, IReadOnlyList<Guid>>());

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

        public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminPlaceResponse?> GetAdminByIdAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Place?> GetByIdAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Place place, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeletePlacePersistenceResult> DeleteForAdminAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlaceReadModel?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
