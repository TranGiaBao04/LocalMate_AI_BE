using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Trips;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripGenerationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // 2026-09-26 08:00 UTC = 15:00 giờ Việt Nam.
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Generate_MissingUser_ReturnsUserNotFoundWithoutRunningEngine()
    {
        var fixture = new Fixture(userExists: false);

        var result = await fixture.Service.GenerateAsync(UserId, Request());

        Assert.Equal(GenerateTripResultStatus.UserNotFound, result.Status);
        Assert.Equal(0, fixture.Engine.Calls);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Generate_ValidationFailed_PropagatesErrorsAndSavesNothing()
    {
        var errors = new Dictionary<string, string[]> { ["DurationHours"] = ["invalid"] };
        var fixture = new Fixture(new FallbackItineraryResult(
            FallbackItineraryStatus.ValidationFailed, ValidationErrors: errors));

        var result = await fixture.Service.GenerateAsync(UserId, Request());

        Assert.Equal(GenerateTripResultStatus.ValidationFailed, result.Status);
        Assert.Same(errors, result.ValidationErrors);
        Assert.Empty(fixture.Trips.Added);
    }

    [Theory]
    [InlineData("OutOfServiceArea", "OutOfServiceArea")]
    [InlineData("InsufficientCandidates", "InsufficientCandidates")]
    [InlineData(null, "InsufficientCandidates")]
    public async Task Generate_NotSufficient_ReturnsNoPlacesWithReasonAndSavesNothing(
        string? engineReason, string expectedReason)
    {
        var fixture = new Fixture(Sufficient(isSufficient: false, reason: engineReason));

        var result = await fixture.Service.GenerateAsync(UserId, Request());

        Assert.Equal(GenerateTripResultStatus.NoPlaces, result.Status);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Generate_UnknownTag_ReturnsInvalidTagsAndSavesNothing()
    {
        var fixture = new Fixture(Sufficient());

        var result = await fixture.Service.GenerateAsync(UserId, Request(tagIds: [Guid.NewGuid()]));

        Assert.Equal(GenerateTripResultStatus.InvalidTags, result.Status);
        Assert.Equal(0, fixture.Engine.Calls); // kiểm tra tag trước matching
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Generate_InvalidPlannedDate_ReturnsValidationErrorWithoutRunningEngine()
    {
        var fixture = new Fixture(Sufficient());

        var result = await fixture.Service.GenerateAsync(UserId, Request(plannedDate: new DateOnly(2026, 9, 25)));

        Assert.Equal(GenerateTripResultStatus.ValidationFailed, result.Status);
        Assert.Contains("PlannedDate", result.ValidationErrors!.Keys);
        Assert.Equal(0, fixture.Engine.Calls);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Generate_WithoutDateOrTime_SavesPlannedStartAtAsTodayAtEightVietnamTime()
    {
        var fixture = new Fixture(Sufficient());

        await fixture.Service.GenerateAsync(UserId, Request());

        Assert.Equal(new DateTime(2026, 9, 26, 8, 0, 0), Assert.Single(fixture.Trips.Added).PlannedStartAt);
    }

    [Fact]
    public async Task Generate_WithDateAndTime_SavesPlannedStartAtAsGiven()
    {
        var fixture = new Fixture(Sufficient());

        await fixture.Service.GenerateAsync(
            UserId, Request(plannedDate: new DateOnly(2026, 10, 3), startTime: new TimeOnly(18, 0)));

        Assert.Equal(new DateTime(2026, 10, 3, 18, 0, 0), Assert.Single(fixture.Trips.Added).PlannedStartAt);
    }

    [Fact]
    public async Task Generate_InactiveTag_ReturnsInvalidTags()
    {
        var inactive = new Tag { Id = Guid.NewGuid(), Name = "Cũ", IsActive = false };
        var fixture = new Fixture(Sufficient(), tags: [inactive]);

        var result = await fixture.Service.GenerateAsync(UserId, Request(tagIds: [inactive.Id]));

        Assert.Equal(GenerateTripResultStatus.InvalidTags, result.Status);
        Assert.Empty(fixture.Trips.Added);
    }

    [Fact]
    public async Task Generate_Success_SavesDraftTripWithItemsAndDistinctTagsThenReturnsDetail()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Cà phê", IsActive = true };
        var fixture = new Fixture(Sufficient(), tags: [tag]);

        var result = await fixture.Service.GenerateAsync(UserId, Request(tagIds: [tag.Id, tag.Id]));

        Assert.Equal(GenerateTripResultStatus.Success, result.Status);
        var saved = Assert.Single(fixture.Trips.Added);
        Assert.Equal(UserId, saved.UserId);
        Assert.Equal(TripStatus.Draft, saved.Status);
        Assert.Equal(TravelMode.Motorbike, saved.TravelMode);
        Assert.Equal(2, saved.Items.Count);
        Assert.Equal(tag.Id, Assert.Single(saved.Tags).TagId);
        Assert.Same(fixture.Detail.Response, result.Response);
        Assert.Equal(saved.Id, fixture.Detail.RequestedTripId);
    }

    private static TripRequestDto Request(
        IReadOnlyList<Guid>? tagIds = null, DateOnly? plannedDate = null, TimeOnly? startTime = null) =>
        new(10.77, 106.69, 6, 0m, 900_000m, tagIds ?? [], TravelMode.Motorbike, plannedDate, startTime);

    private static FallbackItineraryResult Sufficient(bool isSufficient = true, string? reason = null) =>
        new(
            FallbackItineraryStatus.Success,
            new FallbackItineraryPayload(
                "heuristic",
                isSufficient,
                reason,
                "Bến Thành",
                isSufficient ? 2 : 0,
                "Standard",
                isSufficient ? [Stop(0, 8, 0), Stop(1, 9, 5)] : []));

    private static FallbackStopDto Stop(int order, int hour, int minute) =>
        new(
            Guid.NewGuid(), $"Place {order}", "Địa chỉ", 10.77, 106.69, "Cafe",
            order, new TimeOnly(hour, minute), 60, 50_000m, "lý do", 1.0, 100, "Bến Thành");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class Fixture
    {
        public Fixture(
            FallbackItineraryResult? engineResult = null,
            bool userExists = true,
            IReadOnlyList<Tag>? tags = null)
        {
            Engine = new FakeEngine(engineResult ?? Sufficient());
            Trips = new FakeTripRepository();
            Detail = new FakeTripDetailService();
            Service = new TripGenerationService(
                new FakeUserRepository(userExists ? UserId : null),
                new TripRequestValidator(Clock),
                new FakeTagRepository(tags ?? []),
                Engine,
                Trips,
                Detail,
                Clock);
        }

        public TripGenerationService Service { get; }
        public FakeEngine Engine { get; }
        public FakeTripRepository Trips { get; }
        public FakeTripDetailService Detail { get; }
    }

    private sealed class FakeEngine(FallbackItineraryResult result) : IHeuristicFallbackEngine
    {
        public int Calls { get; private set; }

        public Task<FallbackItineraryResult> GenerateFallbackAsync(
            TripRequestDto request, string fallbackReason, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeTagRepository(IReadOnlyList<Tag> tags) : ITagRepository
    {
        public Task<IReadOnlyList<Tag>> GetByIdsAsync(
            IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tag>>(tags.Where(tag => tagIds.Contains(tag.Id)).ToList());

        public Task<IReadOnlyList<Tag>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTripDetailService : ITripDetailService
    {
        public TripDetailResponse Response { get; } = new(
            Guid.NewGuid(), "Draft", "Motorbike", 10.77, 106.69, "Bến Thành", 6, 0m, 900_000m, 100_000m, 120, 120, 5, 125,
            new TimeOnly(10, 5), [], [], DateTime.UtcNow, DateTime.UtcNow, null);

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
