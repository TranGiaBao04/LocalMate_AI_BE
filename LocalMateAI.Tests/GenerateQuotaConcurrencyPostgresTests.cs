using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Trips;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class GenerateQuotaConcurrencyPostgresTests
{
    private const string ConnectionEnvironmentVariable = "LOCALMATE_TEST_CONNECTION";

    [Fact]
    public async Task FreeUser_TwoConcurrentGenerates_ExactlyOneTripAndUsageEventCommit()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var seed = await SeedAsync(connectionString, PlanCode.Free);
        try
        {
            await using var first = CreateScope(connectionString, seed);
            await using var second = CreateScope(connectionString, seed);

            var results = await Task.WhenAll(
                first.Service.GenerateAsync(seed.UserId, Request()),
                second.Service.GenerateAsync(seed.UserId, Request()));

            Assert.Single(results, result => result.Status == GenerateTripResultStatus.Success);
            var blocked = Assert.Single(results, result => result.Status == GenerateTripResultStatus.QuotaExceeded);
            Assert.Equal(1, blocked.Used);
            Assert.Equal(1, blocked.Limit);

            await using var verify = CreateContext(connectionString);
            Assert.Equal(1, await verify.Trips.CountAsync(trip => trip.UserId == seed.UserId));
            var usage = Assert.Single(await verify.UsageEvents
                .AsNoTracking()
                .Where(candidate => candidate.UserId == seed.UserId)
                .ToListAsync());
            Assert.True((usage.CreatedAt - seed.NowUtc).Duration() < TimeSpan.FromMilliseconds(1));

            var subscription = CreateSubscriptionService(verify, seed.NowUtc);
            var readModel = await subscription.GetMySubscriptionAsync(seed.UserId);
            Assert.NotNull(readModel);
            Assert.Equal(1, readModel.Usage.GenerateUsed);
            Assert.Equal(1, readModel.Usage.GenerateLimit);
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [Theory]
    [InlineData(PlanCode.TripPass)]
    [InlineData(PlanCode.Membership)]
    public async Task PaidUser_TwoConcurrentGenerates_BothCommitWithoutUsageEvents(PlanCode planCode)
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var seed = await SeedAsync(connectionString, planCode);
        try
        {
            await using var first = CreateScope(connectionString, seed);
            await using var second = CreateScope(connectionString, seed);

            var results = await Task.WhenAll(
                first.Service.GenerateAsync(seed.UserId, Request()),
                second.Service.GenerateAsync(seed.UserId, Request()));

            Assert.All(results, result => Assert.Equal(GenerateTripResultStatus.Success, result.Status));
            await using var verify = CreateContext(connectionString);
            Assert.Equal(2, await verify.Trips.CountAsync(trip => trip.UserId == seed.UserId));
            Assert.Equal(0, await verify.UsageEvents.CountAsync(usage => usage.UserId == seed.UserId));
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [Fact]
    public async Task FreeUser_DeletingGeneratedTrip_DoesNotRefundQuota()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var seed = await SeedAsync(connectionString, PlanCode.Free);
        try
        {
            await using (var first = CreateScope(connectionString, seed))
            {
                var result = await first.Service.GenerateAsync(seed.UserId, Request());
                Assert.Equal(GenerateTripResultStatus.Success, result.Status);
                Assert.True(await new TripRepository(first.Context)
                    .SoftDeleteAsync(result.Response!.Id, seed.UserId));
            }

            await using var second = CreateScope(connectionString, seed);
            var retry = await second.Service.GenerateAsync(seed.UserId, Request());

            Assert.Equal(GenerateTripResultStatus.QuotaExceeded, retry.Status);
            await using var verify = CreateContext(connectionString);
            Assert.Equal(1, await verify.UsageEvents.CountAsync(usage => usage.UserId == seed.UserId));
            Assert.Equal(1, await verify.Trips.CountAsync(trip => trip.UserId == seed.UserId));
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [Fact]
    public async Task FreeUser_UsageInsertFailure_RollsBackFlushedTrip()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var seed = await SeedAsync(connectionString, PlanCode.Free);
        try
        {
            await using (var scope = CreateScope(
                connectionString,
                seed,
                context => new FailingUsageEventRepository(context)))
            {
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    scope.Service.GenerateAsync(seed.UserId, Request()));
            }

            await using var verify = CreateContext(connectionString);
            Assert.Equal(0, await verify.Trips.CountAsync(trip => trip.UserId == seed.UserId));
            Assert.Equal(0, await verify.UsageEvents.CountAsync(usage => usage.UserId == seed.UserId));
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [Fact]
    public async Task FreeUser_VietnamMonthBoundary_ResetsAtSeventeenUtc()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var beforeBoundary = new DateTime(2026, 9, 30, 16, 59, 59, DateTimeKind.Utc);
        var afterBoundary = new DateTime(2026, 9, 30, 17, 0, 1, DateTimeKind.Utc);
        var seed = await SeedAsync(connectionString, PlanCode.Free, beforeBoundary);
        try
        {
            await using (var september = CreateScope(connectionString, seed))
            {
                var result = await september.Service.GenerateAsync(seed.UserId, Request());
                Assert.Equal(GenerateTripResultStatus.Success, result.Status);
            }

            var octoberSeed = seed with { NowUtc = afterBoundary };
            await using (var october = CreateScope(connectionString, octoberSeed))
            {
                var result = await october.Service.GenerateAsync(seed.UserId, Request());
                Assert.Equal(GenerateTripResultStatus.Success, result.Status);
            }

            await using var blockedScope = CreateScope(connectionString, octoberSeed);
            var blocked = await blockedScope.Service.GenerateAsync(seed.UserId, Request());
            Assert.Equal(GenerateTripResultStatus.QuotaExceeded, blocked.Status);
            Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), blocked.ResetAt);

            await using var verify = CreateContext(connectionString);
            Assert.Equal(2, await verify.UsageEvents.CountAsync(usage => usage.UserId == seed.UserId));
            var subscription = await CreateSubscriptionService(verify, afterBoundary)
                .GetMySubscriptionAsync(seed.UserId);
            Assert.NotNull(subscription);
            Assert.Equal(1, subscription.Usage.GenerateUsed);
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [Fact]
    public async Task PaymentAndGenerate_ForSameUser_ShareLockOrderWithoutDeadlock()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
        {
            return;
        }

        var seed = await SeedAsync(connectionString, PlanCode.Free);
        var paymentEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePayment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var paymentContext = CreateContext(connectionString);
            await using var generate = CreateScope(connectionString, seed);
            var payment = new PaymentOperationExecutor(paymentContext).ExecuteForUserAsync(
                seed.UserId,
                async cancellationToken =>
                {
                    paymentEntered.TrySetResult();
                    await releasePayment.Task.WaitAsync(cancellationToken);
                    return true;
                });

            await paymentEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var generation = generate.Service.GenerateAsync(seed.UserId, Request());
            await Task.Delay(100);
            Assert.False(generation.IsCompleted);

            releasePayment.TrySetResult();
            var paymentResult = await payment.WaitAsync(TimeSpan.FromSeconds(10));
            var generationResult = await generation.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(paymentResult.PersistedUserExists);
            Assert.Equal(GenerateTripResultStatus.Success, generationResult.Status);
        }
        finally
        {
            releasePayment.TrySetResult();
            await CleanupAsync(connectionString, seed);
        }
    }

    private static string? GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }

    private static TripRequestDto Request() =>
        new(10.77, 106.69, 6, 0m, 900_000m, [], TravelMode.Motorbike);

    private static async Task<SeedData> SeedAsync(
        string connectionString,
        PlanCode planCode,
        DateTime? now = null)
    {
        var nowUtc = now ?? DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var placeIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using var context = CreateContext(connectionString);
        context.Users.Add(new User
        {
            Id = userId,
            FullName = "Generate quota concurrency test",
            Email = $"generate-quota-{userId:N}@localmate.test",
            Role = UserRole.User
        });
        context.Places.AddRange(placeIds.Select((placeId, index) => new Place
        {
            Id = placeId,
            Name = $"Generate quota place {index}",
            Address = "Test address",
            Location = new Point(106.69 + index * 0.001, 10.77) { SRID = 4326 },
            Category = PlaceCategory.Cafe,
            Status = PlaceStatus.Active,
            IsVerified = true,
            EstimatedCostMin = 20_000,
            EstimatedCostMax = 50_000
        }));
        if (planCode != PlanCode.Free)
        {
            context.UserSubscriptions.Add(new UserSubscription
            {
                UserId = userId,
                PlanCode = planCode,
                StartsAt = nowUtc.AddDays(-1),
                EndsAt = nowUtc.AddDays(7)
            });
        }

        await context.SaveChangesAsync();
        return new SeedData(userId, placeIds, nowUtc);
    }

    private static GenerateScope CreateScope(
        string connectionString,
        SeedData seed,
        Func<AppDbContext, IUsageEventRepository>? usageRepositoryFactory = null)
    {
        var context = CreateContext(connectionString);
        var clock = new FixedTimeProvider(seed.NowUtc);
        var usageRepository = usageRepositoryFactory?.Invoke(context) ?? new UsageEventRepository(context);
        var service = new TripGenerationService(
            new UserRepository(context),
            new TripRequestValidator(clock),
            new EmptyTagRepository(),
            new SuccessfulEngine(seed.PlaceIds),
            new TripRepository(context),
            new SubscriptionRepository(context),
            usageRepository,
            new TripGenerationQuotaExecutor(context),
            new SuccessfulDetailService(),
            clock);
        return new GenerateScope(context, service);
    }

    private static SubscriptionService CreateSubscriptionService(AppDbContext context, DateTime nowUtc) =>
        new(
            new UserRepository(context),
            new SubscriptionRepository(context),
            new UsageEventRepository(context),
            new TripRepository(context),
            new FixedTimeProvider(nowUtc));

    private static async Task CleanupAsync(string connectionString, SeedData seed)
    {
        await using var context = CreateContext(connectionString);
        await context.UsageEvents.Where(usage => usage.UserId == seed.UserId).ExecuteDeleteAsync();
        await context.Trips.Where(trip => trip.UserId == seed.UserId).ExecuteDeleteAsync();
        await context.UserSubscriptions
            .Where(subscription => subscription.UserId == seed.UserId)
            .ExecuteDeleteAsync();
        await context.Places.Where(place => seed.PlaceIds.Contains(place.Id)).ExecuteDeleteAsync();
        await context.Users.Where(user => user.Id == seed.UserId).ExecuteDeleteAsync();
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private sealed record SeedData(Guid UserId, Guid[] PlaceIds, DateTime NowUtc);

    private sealed record GenerateScope(AppDbContext Context, TripGenerationService Service)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc, TimeSpan.Zero);
    }

    private sealed class EmptyTagRepository : ITagRepository
    {
        public Task<IReadOnlyList<Tag>> GetByIdsAsync(
            IReadOnlyCollection<Guid> tagIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tag>>([]);

        public Task<IReadOnlyList<Tag>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tag>>([]);
    }

    private sealed class SuccessfulEngine(IReadOnlyList<Guid> placeIds) : IHeuristicFallbackEngine
    {
        public Task<FallbackItineraryResult> GenerateFallbackAsync(
            TripRequestDto request,
            string fallbackReason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FallbackItineraryResult(
                FallbackItineraryStatus.Success,
                new FallbackItineraryPayload(
                    fallbackReason,
                    true,
                    null,
                    "Bến Thành",
                    2,
                    "Standard",
                    placeIds.Select((placeId, index) => new FallbackStopDto(
                        placeId,
                        $"Place {index}",
                        "Test address",
                        10.77,
                        106.69 + index * 0.001,
                        PlaceCategory.Cafe.ToString(),
                        index,
                        new TimeOnly(8 + index, 0),
                        45,
                        30_000,
                        "Test",
                        1,
                        100,
                        "Bến Thành")).ToList())));
    }

    private sealed class SuccessfulDetailService : ITripDetailService
    {
        public Task<GetTripDetailResult> GetAsync(
            Guid userId,
            Guid tripId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetTripDetailResult.Succeeded(new TripDetailResponse(
                tripId,
                TripStatus.Draft.ToString(),
                TravelMode.Motorbike.ToString(),
                10.77,
                106.69,
                "Bến Thành",
                6,
                0,
                900_000,
                60_000,
                90,
                90,
                0,
                90,
                new TimeOnly(9, 30),
                [],
                [],
                DateTime.UtcNow,
                DateTime.UtcNow,
                null)));
    }

    private sealed class FailingUsageEventRepository(AppDbContext context) : IUsageEventRepository
    {
        private readonly UsageEventRepository inner = new(context);

        public Task<int> CountAsync(
            Guid userId,
            UsageEventType type,
            DateTime startUtc,
            DateTime nextStartUtc,
            CancellationToken cancellationToken = default) =>
            inner.CountAsync(userId, type, startUtc, nextStartUtc, cancellationToken);

        public async Task AddAsync(UsageEvent usageEvent, CancellationToken cancellationToken = default)
        {
            usageEvent.TripId = Guid.NewGuid();
            context.UsageEvents.Add(usageEvent);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
