using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripDetailServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Get_EmptyTripId_ReturnsInvalidWithoutRepositoryCall()
    {
        var repository = new FakeTripRepository(null);
        var service = new TripDetailService(new FakeUserRepository(UserId), repository);

        var result = await service.GetAsync(UserId, Guid.Empty);

        Assert.Equal(GetTripDetailResultStatus.InvalidTripId, result.Status);
        Assert.Equal(0, repository.ReadCalls);
    }

    [Fact]
    public async Task Get_NonPersistedUser_ReturnsUserNotFoundBeforeTripLookup()
    {
        var repository = new FakeTripRepository(null);
        var service = new TripDetailService(new FakeUserRepository(), repository);

        var result = await service.GetAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(GetTripDetailResultStatus.UserNotFound, result.Status);
        Assert.Equal(0, repository.ReadCalls);
    }

    [Fact]
    public async Task Get_MissingOrForeignOrDeletedTrip_ReturnsTripNotFound()
    {
        var repository = new FakeTripRepository(null);
        var service = new TripDetailService(new FakeUserRepository(UserId), repository);

        var result = await service.GetAsync(UserId, Guid.NewGuid());

        Assert.Equal(GetTripDetailResultStatus.TripNotFound, result.Status);
        Assert.Equal(1, repository.ReadCalls);
    }

    [Fact]
    public async Task Get_OwnedTrip_MapsItemsAndComputesTotals()
    {
        var tripId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var model = new TripDetailReadModel(
            tripId,
            TripStatus.Finalized,
            10.80,
            106.65,
            "Bến Thành",
            4,
            0m,
            300_000m,
            [tagId],
            [
                Item(0, new TimeOnly(8, 0), 90, 50_000m, isVisited: true),
                Item(1, new TimeOnly(9, 30), 60, 120_000m, isVisited: false)
            ],
            new DateTime(2026, 9, 24, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 24, 2, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 24, 2, 0, 0, DateTimeKind.Utc));
        var service = new TripDetailService(new FakeUserRepository(UserId), new FakeTripRepository(model));

        var result = await service.GetAsync(UserId, tripId);

        Assert.Equal(GetTripDetailResultStatus.Success, result.Status);
        var response = result.Response!;
        Assert.Equal("Finalized", response.Status);
        Assert.Equal("Bến Thành", response.StationName);
        Assert.Equal(170_000m, response.EstimatedBudget);
        Assert.Equal(150, response.TotalDurationMinutes);
        Assert.Equal([tagId], response.TagIds);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal("Cafe", response.Items[0].Category);
        Assert.True(response.Items[0].IsVisited);
        Assert.False(response.Items[1].IsVisited);
        Assert.Equal(model.FinalizedAt, response.FinalizedAt);
    }

    [Fact]
    public async Task Get_OwnedTrip_ReportsTravelPerLegAndTotals()
    {
        var tripId = Guid.NewGuid();
        var model = new TripDetailReadModel(
            tripId,
            TripStatus.Draft,
            10.80,
            106.65,
            "Bến Thành",
            4,
            0m,
            300_000m,
            [],
            [
                Item(0, new TimeOnly(8, 0), 90, 50_000m, isVisited: false),
                Item(1, new TimeOnly(9, 45), 60, 120_000m, isVisited: false, latitude: 10.79) // trống 15' sau 09:30
            ],
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            TravelMode.Motorbike);
        var service = new TripDetailService(new FakeUserRepository(UserId), new FakeTripRepository(model));

        var response = (await service.GetAsync(UserId, tripId)).Response!;

        Assert.Equal("Motorbike", response.TravelMode);
        Assert.Null(response.Items[0].TravelMinutesFromPrevious);
        Assert.Null(response.Items[0].DistanceMetersFromPrevious);
        Assert.Equal(15, response.Items[1].TravelMinutesFromPrevious);
        Assert.Equal(2890, response.Items[1].DistanceMetersFromPrevious); // ~2,22 km chim bay × 1,3
        Assert.Equal(37, response.Items[1].WalkingMinutes);
        Assert.Equal(8, response.Items[1].MotorbikeMinutes);
        Assert.Equal(150, response.TotalVisitMinutes);
        Assert.Equal(15, response.TotalTravelMinutes);
        Assert.Equal(165, response.TotalMinutes);
        Assert.Equal(new TimeOnly(10, 45), response.EndTime);
    }

    private static TripDetailReadModel Model(Guid tripId, DateTime? plannedStartAt, TimeOnly firstStopTime) =>
        new(
            tripId,
            TripStatus.Draft,
            10.80,
            106.65,
            "Bến Thành",
            4,
            0m,
            300_000m,
            [],
            [
                Item(0, firstStopTime, 90, 50_000m, isVisited: false),
                Item(1, firstStopTime.AddMinutes(105), 60, 120_000m, isVisited: false) // trống 15' sau chặng đầu
            ],
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            TravelMode.Auto,
            plannedStartAt);

    [Fact]
    public async Task Get_TripWithPlannedStart_ReportsDateTimeAndTravelFromOriginInsideTotals()
    {
        var tripId = Guid.NewGuid();
        var model = Model(tripId, new DateTime(2026, 10, 3, 8, 0, 0), new TimeOnly(8, 20));
        var service = new TripDetailService(new FakeUserRepository(UserId), new FakeTripRepository(model));

        var response = (await service.GetAsync(UserId, tripId)).Response!;

        Assert.Equal(new DateOnly(2026, 10, 3), response.PlannedDate);
        Assert.Equal(new TimeOnly(8, 0), response.StartTime);
        Assert.Equal(20, response.TravelMinutesFromOrigin);
        Assert.Null(response.Items[0].TravelMinutesFromPrevious); // đoạn đi tới chặng đầu không nằm ở chặng
        Assert.Equal(15 + 20, response.TotalTravelMinutes);
        Assert.Equal(150 + 15 + 20, response.TotalMinutes);
        Assert.Equal(new TimeOnly(11, 5), response.EndTime);
    }

    [Fact]
    public async Task Get_TripWithoutPlannedStart_LeavesNewFieldsNullAndTotalsUnchanged()
    {
        var tripId = Guid.NewGuid();
        var model = Model(tripId, null, new TimeOnly(8, 20));
        var service = new TripDetailService(new FakeUserRepository(UserId), new FakeTripRepository(model));

        var response = (await service.GetAsync(UserId, tripId)).Response!;

        Assert.Null(response.PlannedDate);
        Assert.Null(response.StartTime);
        Assert.Null(response.TravelMinutesFromOrigin);
        Assert.Equal(15, response.TotalTravelMinutes);
        Assert.Equal(165, response.TotalMinutes);
    }

    [Fact]
    public async Task Get_PlannedStartLaterThanFirstStop_ClampsTravelFromOriginToZero()
    {
        var tripId = Guid.NewGuid();
        var model = Model(tripId, new DateTime(2026, 10, 3, 9, 0, 0), new TimeOnly(8, 20));
        var service = new TripDetailService(new FakeUserRepository(UserId), new FakeTripRepository(model));

        var response = (await service.GetAsync(UserId, tripId)).Response!;

        Assert.Equal(0, response.TravelMinutesFromOrigin);
        Assert.Equal(15, response.TotalTravelMinutes);
    }

    private static TripItemReadModel Item(
        int order,
        TimeOnly time,
        int minutes,
        decimal budget,
        bool isVisited,
        double latitude = 10.77) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"Place {order}",
            PlaceCategory.Cafe,
            null,
            latitude,
            106.69,
            "Bến Thành",
            order,
            time,
            minutes,
            budget,
            "reason",
            isVisited,
            isVisited ? DateTimeOffset.UtcNow : null);

    private sealed class FakeTripRepository(TripDetailReadModel? detail) : ITripRepository
    {
        public Task<int> CountFinalizedByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public int ReadCalls { get; private set; }

        public Task<TripDetailReadModel?> GetOwnedDetailAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(detail);
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

        public Task AddAsync(Trip trip, CancellationToken cancellationToken = default) =>
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
