using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class CuratedTripServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

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
            new CuratedPlaceForApplyReadModel(Guid.NewGuid(), 0, 10.77, 106.70, 50_000m),
            new CuratedPlaceForApplyReadModel(Guid.NewGuid(), 1, 10.78, 106.69, 30_000m)
        ]);
        var fixture = new Fixture(source: source);

        var result = await fixture.Service.ApplyAsync(
            UserId,
            source.Id,
            new ApplyCuratedItineraryRequest(10.80, 106.65, new TimeOnly(9, 0)));

        Assert.Equal(ApplyCuratedItineraryResultStatus.Success, result.Status);
        var saved = Assert.Single(fixture.Trips.Added);
        Assert.Equal(UserId, saved.UserId);
        Assert.Equal(10.80, saved.StartLatitude);
        Assert.Equal(2, saved.Items.Count);
        Assert.Equal(new TimeOnly(9, 0), saved.Items.OrderBy(item => item.OrderIndex).First().ScheduledTime);
        Assert.Same(fixture.Detail.Response, result.Response);
        Assert.Equal(saved.Id, fixture.Detail.RequestedTripId);
    }

    private static CuratedItineraryForApplyReadModel Source(
        IReadOnlyList<CuratedPlaceForApplyReadModel> places) =>
        new(Guid.NewGuid(), "Mẫu", 240, 0m, 300_000m, places);

    private sealed class Fixture
    {
        public Fixture(bool userExists = true, CuratedItineraryForApplyReadModel? source = null)
        {
            Curated = new FakeCuratedRepository(source);
            Trips = new FakeTripRepository();
            Detail = new FakeTripDetailService();
            Service = new CuratedTripService(
                new FakeUserRepository(userExists ? UserId : null),
                Curated,
                Trips,
                Detail,
                new CoordinatesValidationService());
        }

        public CuratedTripService Service { get; }
        public FakeCuratedRepository Curated { get; }
        public FakeTripRepository Trips { get; }
        public FakeTripDetailService Detail { get; }
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
            Guid.NewGuid(), "Draft", 10.80, 106.65, "Bến Thành", 4, 0m, 300_000m, 80_000m, 160, 160, 0, 160, null,
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
