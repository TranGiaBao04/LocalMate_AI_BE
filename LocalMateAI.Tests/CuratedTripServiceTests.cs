using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class CuratedTripServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // 2026-09-26 08:00 UTC = 15:00 giờ Việt Nam.
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Apply_EmptyCuratedId_ReturnsInvalidIdWithoutAnyLookup()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ApplyAsync(UserId, Guid.Empty, null);

        Assert.Equal(ApplyCuratedItineraryResultStatus.InvalidId, result.Status);
        Assert.Equal(0, fixture.Curated.ReadCalls);
    }

    [Theory]
    [InlineData(10.78, null)]
    [InlineData(null, 106.70)]
    public async Task Apply_OnlyOneCoordinate_ReturnsInvalidStart(double? latitude, double? longitude)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(latitude, longitude, null));

        Assert.Equal(ApplyCuratedItineraryResultStatus.InvalidStart, result.Status);
        Assert.Equal(0, fixture.Curated.ReadCalls);
    }

    [Fact]
    public async Task Apply_CoordinateOutsideHoChiMinhCity_ReturnsInvalidStart()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(21.03, 105.85, null)); // Hà Nội

        Assert.Equal(ApplyCuratedItineraryResultStatus.InvalidStart, result.Status);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Apply_NonPersistedUser_ReturnsUserNotFoundBeforeReadingItinerary()
    {
        var fixture = new Fixture(userExists: false);

        var result = await fixture.Service.ApplyAsync(UserId, Guid.NewGuid(), null);

        Assert.Equal(ApplyCuratedItineraryResultStatus.UserNotFound, result.Status);
        Assert.Equal(0, fixture.Curated.ReadCalls);
    }

    [Fact]
    public async Task Apply_MissingItinerary_ReturnsItineraryNotFound()
    {
        var fixture = new Fixture(source: null);

        var result = await fixture.Service.ApplyAsync(UserId, Guid.NewGuid(), null);

        Assert.Equal(ApplyCuratedItineraryResultStatus.ItineraryNotFound, result.Status);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Apply_ItineraryWithoutAvailablePlaces_ReturnsUnavailableWithoutWrite()
    {
        var fixture = new Fixture(source: Source(places: []));

        var result = await fixture.Service.ApplyAsync(UserId, Guid.NewGuid(), null);

        Assert.Equal(ApplyCuratedItineraryResultStatus.ItineraryUnavailable, result.Status);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Apply_ValidRequest_SavesDraftTripOwnedByUserAndReturnsDetail()
    {
        var source = Source(places:
        [
            new CuratedPlaceForApplyReadModel(Guid.NewGuid(), 0, 10.77, 106.70, 50_000m, PlaceCategory.Cafe),
            new CuratedPlaceForApplyReadModel(Guid.NewGuid(), 1, 10.78, 106.69, 30_000m, PlaceCategory.Food)
        ]);
        var fixture = new Fixture(source: source);

        var result = await fixture.Service.ApplyAsync(
            UserId,
            source.Id,
            new ApplyCuratedItineraryRequest(10.80, 106.65, new TimeOnly(18, 0)));

        Assert.Equal(ApplyCuratedItineraryResultStatus.Success, result.Status);
        var saved = Assert.Single(fixture.Trips.Added);
        Assert.Equal(UserId, saved.UserId);
        Assert.Equal(10.80, saved.StartLatitude);
        Assert.Equal(2, saved.Items.Count);
        // Xuất phát 18:00 từ toạ độ user; chặng đầu bắt đầu sau khi cộng thời gian đi tới đó.
        Assert.True(saved.Items.OrderBy(item => item.OrderIndex).First().ScheduledTime > new TimeOnly(18, 0));
        Assert.Equal(new DateTime(2026, 9, 26, 18, 0, 0), saved.PlannedStartAt);
        Assert.Equal(TravelMode.Auto, saved.TravelMode);
        Assert.Same(fixture.Detail.Response, result.Response);
        Assert.Equal(saved.Id, fixture.Detail.RequestedTripId);
    }

    [Fact]
    public async Task Apply_CoordinateFarFromAnyStation_ReturnsOutOfServiceAreaWithoutReadingItinerary()
    {
        var fixture = new Fixture(source: Source([Place(0)]), withinServiceArea: false);

        var result = await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(10.80, 106.65, null));

        Assert.Equal(ApplyCuratedItineraryResultStatus.OutOfServiceArea, result.Status);
        Assert.Equal(0, fixture.Curated.ReadCalls);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Apply_PastPlannedDate_ReturnsValidationErrorOnPlannedDate()
    {
        var fixture = new Fixture(source: Source([Place(0)]));

        var result = await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(null, null, null, new DateOnly(2026, 9, 25)));

        Assert.Equal(ApplyCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Contains("PlannedDate", result.ValidationErrors!.Keys);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Apply_StartTimeInThePastToday_ReturnsValidationErrorOnStartTime()
    {
        var fixture = new Fixture(source: Source([Place(0)]));

        var result = await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(null, null, new TimeOnly(9, 0)));

        Assert.Equal(ApplyCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Contains("StartTime", result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Apply_ItineraryRunningPastMidnight_ReturnsValidationErrorOnStartTimeAndSavesNothing()
    {
        // 3 địa điểm Culture (3 x 90') + 2 phút đi = 272 phút; 22:00 + 272' vượt 24:00.
        var fixture = new Fixture(source: Source([Place(0), Place(1), Place(2)]));

        var result = await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(null, null, new TimeOnly(22, 0)));

        Assert.Equal(ApplyCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Contains("StartTime", result.ValidationErrors!.Keys);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Apply_WithoutCoordinates_FirstStopStartsExactlyAtStartTime()
    {
        var fixture = new Fixture(source: Source([Place(0)]));

        await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(null, null, new TimeOnly(18, 0)));

        Assert.Equal(new TimeOnly(18, 0), Assert.Single(fixture.Trips.Added).Items.Single().ScheduledTime);
    }

    [Fact]
    public async Task Apply_WithDateAndTravelMode_SavesThemOnTheTrip()
    {
        var fixture = new Fixture(source: Source([Place(0)]));

        await fixture.Service.ApplyAsync(
            UserId,
            Guid.NewGuid(),
            new ApplyCuratedItineraryRequest(null, null, new TimeOnly(8, 0), new DateOnly(2026, 10, 3), TravelMode.Walking));

        var saved = Assert.Single(fixture.Trips.Added);
        Assert.Equal(new DateTime(2026, 10, 3, 8, 0, 0), saved.PlannedStartAt);
        Assert.Equal(TravelMode.Walking, saved.TravelMode);
    }

    [Fact]
    public async Task Apply_UnknownTravelMode_ReturnsValidationErrorOnTravelMode()
    {
        var fixture = new Fixture(source: Source([Place(0)]));

        var result = await fixture.Service.ApplyAsync(
            UserId, Guid.NewGuid(), new ApplyCuratedItineraryRequest(null, null, null, null, (TravelMode)99));

        Assert.Equal(ApplyCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Contains("TravelMode", result.ValidationErrors!.Keys);
    }

    private static CuratedPlaceForApplyReadModel Place(int order) =>
        new(Guid.NewGuid(), order, 10.77, 106.70, 50_000m, PlaceCategory.Culture);

    private static CuratedItineraryForApplyReadModel Source(
        IReadOnlyList<CuratedPlaceForApplyReadModel> places) =>
        new(Guid.NewGuid(), "Mẫu", 240, 0m, 300_000m, places);

    private sealed class Fixture
    {
        public Fixture(
            bool userExists = true,
            CuratedItineraryForApplyReadModel? source = null,
            bool withinServiceArea = true)
        {
            Curated = new FakeCuratedRepository(source);
            Trips = new FakeTripRepository();
            Detail = new FakeTripDetailService();
            Service = new CuratedTripService(
                new FakeUserRepository(userExists ? UserId : null),
                Curated,
                Trips,
                Detail,
                new CoordinatesValidationService(),
                new FakeOrigin(withinServiceArea),
                Clock);
        }

        public CuratedTripService Service { get; }
        public FakeCuratedRepository Curated { get; }
        public FakeTripRepository Trips { get; }
        public FakeTripDetailService Detail { get; }
    }

    private sealed class FakeOrigin(bool withinServiceArea) : ITripOriginResolverService
    {
        public Task<TripOriginResolution?> ResolveAsync(
            TripRequestDto request, CancellationToken cancellationToken = default) =>
            ResolveAsync(request.StartLatitude, request.StartLongitude, cancellationToken);

        public Task<TripOriginResolution?> ResolveAsync(
            double latitude, double longitude, CancellationToken cancellationToken = default) =>
            Task.FromResult<TripOriginResolution?>(new TripOriginResolution(
                new NearestStationResult(Guid.NewGuid(), "Bến Thành", 10.7721, 106.6980, withinServiceArea ? 300 : 50_000),
                withinServiceArea));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeCuratedRepository(CuratedItineraryForApplyReadModel? source) : ICuratedItineraryRepository
    {
        public int ReadCalls { get; private set; }

        public Task<CuratedItineraryForApplyReadModel?> GetForApplyAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(source);
        }

        public Task<IReadOnlyList<CuratedItineraryResponse>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTripDetailService : ITripDetailService
    {
        public TripDetailResponse Response { get; } = new(
            Guid.NewGuid(), "Draft", "Auto", 10.80, 106.65, "Bến Thành", 4, 0m, 300_000m, 80_000m, 160, 160, 0, 160, null,
            [], [], DateTime.UtcNow, DateTime.UtcNow, null);

        public Guid? RequestedTripId { get; private set; }

        public Task<GetTripDetailResult> GetAsync(Guid userId, Guid tripId, CancellationToken cancellationToken = default)
        {
            RequestedTripId = tripId;
            return Task.FromResult(GetTripDetailResult.Succeeded(Response));
        }
    }

    private sealed class FakeTripRepository : ITripRepository
    {
        public Task<int> CountFinalizedByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public List<Trip> Added { get; } = [];

        public Task AddAsync(Trip trip, CancellationToken cancellationToken = default)
        {
            Added.Add(trip);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AttachUserIfUnownedAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> FinalizeTripAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(Guid itemId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkItineraryItemVisitedIfEligibleAsync(Guid itemId, Guid userId, DateTimeOffset visitedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Trip?> ForkTripAsync(Guid sourceTripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TripDetailReadModel?> GetOwnedDetailAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SoftDeleteAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUserRepository(Guid? userId = null) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid requestedUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == requestedUserId ? new User { Id = requestedUserId } : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
