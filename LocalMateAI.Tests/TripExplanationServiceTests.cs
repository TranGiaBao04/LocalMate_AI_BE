using System.Text.Json;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripExplanationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly TripExplanationStopReadModel Cafe =
        Stop("Tonkin", "Không gian yên tĩnh, ấm cúng", ["Cà phê"]);
    private static readonly TripExplanationStopReadModel Park =
        Stop("Công viên", "Công viên thoáng mát", []);
    private static readonly TripExplanationStopReadModel NoData =
        Stop("Điểm chưa có dữ liệu", null, []);

    [Fact]
    public async Task Success_WritesReasons_MarksTheTrip_AndLogsTheCall()
    {
        var fixture = new Fixture(Trip(Cafe, Park));
        fixture.Llm.Answer = Answer((Cafe, true, "Quán yên tĩnh, hợp ngồi làm việc."), (Park, true, "Thoáng mát, dễ chịu."));

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.Success, result.Status);
        Assert.Equal(Now.UtcDateTime, result.Response!.AiExplainedAt);
        Assert.Equal(
            [
                new ExplainedItemResponse(Cafe.ItemId, Cafe.PlaceId, "Quán yên tĩnh, hợp ngồi làm việc."),
                new ExplainedItemResponse(Park.ItemId, Park.PlaceId, "Thoáng mát, dễ chịu.")
            ],
            result.Response.Items);

        var applied = Assert.Single(fixture.Repository.Applied);
        Assert.Equal(Now.UtcDateTime, applied.ExplainedAt);
        Assert.Equal(2, applied.Updates.Count);

        var record = Assert.Single(fixture.Guard.Records);
        Assert.Equal((UserId, LlmCallKind.Explain, (Guid?)TripId, LlmCallOutcome.Succeeded), (record.UserId, record.Kind, record.TripId, record.Outcome));
        Assert.Equal(1100, record.Response!.InputTokens);

        var request = Assert.Single(fixture.Llm.Requests);
        Assert.Equal(TripExplanationPrompt.SystemInstruction, request.SystemInstruction);
        Assert.Equal(TripExplanationPrompt.MaxOutputTokens, request.MaxOutputTokens);
        Assert.Contains(Cafe.PlaceId.ToString(), request.Input);
    }

    [Fact]
    public async Task EmptyTripId_IsInvalid_WithoutAnyLookup()
    {
        var fixture = new Fixture(Trip(Cafe));

        var result = await fixture.Service.ExplainAsync(UserId, Guid.Empty);

        Assert.Equal(ExplainTripResultStatus.InvalidId, result.Status);
        Assert.Equal(0, fixture.Repository.Reads);
    }

    [Fact]
    public async Task NonPersistedUser_IsRejectedBeforeTripLookup()
    {
        var fixture = new Fixture(Trip(Cafe), userExists: false);

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.UserNotFound, result.Status);
        Assert.Equal(0, fixture.Repository.Reads);
    }

    [Fact]
    public async Task MissingOrForeignTrip_IsNotFound_WithoutCheckingUsage()
    {
        var fixture = new Fixture(trip: null);

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.TripNotFound, result.Status);
        Assert.Empty(fixture.Guard.Checks);
    }

    [Fact]
    public async Task FinalizedTrip_IsRejected_WithoutCallingTheModel()
    {
        var fixture = new Fixture(Trip(Cafe) with { Status = TripStatus.Finalized });

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.TripFinalized, result.Status);
        Assert.Empty(fixture.Llm.Requests);
    }

    [Theory]
    [InlineData(AiUsageStatus.Disabled, ExplainTripResultStatus.AiUnavailable)]
    [InlineData(AiUsageStatus.TripLimitReached, ExplainTripResultStatus.TripLimitReached)]
    [InlineData(AiUsageStatus.DailyLimitReached, ExplainTripResultStatus.DailyLimitReached)]
    public async Task UsageNotAllowed_StopsBeforeCallingTheModel(AiUsageStatus usage, ExplainTripResultStatus expected)
    {
        var resetAt = new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc);
        var fixture = new Fixture(Trip(Cafe));
        fixture.Guard.Decision = new AiUsageDecision(usage, usage == AiUsageStatus.DailyLimitReached ? resetAt : null);

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(expected, result.Status);
        Assert.Equal(usage == AiUsageStatus.DailyLimitReached ? resetAt : null, result.ResetAtUtc);
        Assert.Equal((UserId, LlmCallKind.Explain, (Guid?)TripId), Assert.Single(fixture.Guard.Checks));
        Assert.Empty(fixture.Llm.Requests);
        Assert.Empty(fixture.Guard.Records);
        Assert.Empty(fixture.Repository.Applied);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsUnavailable_LogsTheAttempt_AndWritesNothing()
    {
        var fixture = new Fixture(Trip(Cafe));
        fixture.Llm.Failure = new LlmUnavailableException("high demand");

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.AiUnavailable, result.Status);
        var record = Assert.Single(fixture.Guard.Records);
        Assert.Equal(LlmCallOutcome.ProviderFailed, record.Outcome);
        Assert.Null(record.Response);
        Assert.Empty(fixture.Repository.Applied);
    }

    [Fact]
    public async Task ProviderSlowerThanTimeout_IsTreatedAsFailure()
    {
        var fixture = new Fixture(Trip(Cafe));
        // Giả lập nhà cung cấp bị huỷ do hết giờ (token của người gọi không bị huỷ).
        fixture.Llm.Failure = new OperationCanceledException("timed out");

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.AiUnavailable, result.Status);
        Assert.Equal(LlmCallOutcome.ProviderFailed, Assert.Single(fixture.Guard.Records).Outcome);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_WithoutLogging()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(Trip(Cafe));
        fixture.Llm.BeforeAnswer = cancellation.Cancel;
        fixture.Llm.Failure = new OperationCanceledException("cancelled by caller");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ExplainAsync(UserId, TripId, cancellation.Token));

        Assert.Empty(fixture.Guard.Records);
        Assert.Empty(fixture.Repository.Applied);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"stops\":[]}")]
    public async Task UnusableAnswer_ReturnsUnavailable_AndIsLoggedAsInvalidOutput(string answer)
    {
        var fixture = new Fixture(Trip(Cafe));
        fixture.Llm.Answer = answer;

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.AiUnavailable, result.Status);
        Assert.Equal(LlmCallOutcome.InvalidOutput, Assert.Single(fixture.Guard.Records).Outcome);
        Assert.Empty(fixture.Repository.Applied);
    }

    [Fact]
    public async Task StopWithoutAnyData_GetsTheFixedSentence_AndIsNeverSentToTheModel()
    {
        var fixture = new Fixture(Trip(Cafe, NoData));
        fixture.Llm.Answer = Answer((Cafe, true, "Quán yên tĩnh."));

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.Success, result.Status);
        Assert.Equal(
            ["Quán yên tĩnh.", TripExplanationPrompt.InsufficientDataReason],
            result.Response!.Items.Select(item => item.Reasoning));
        var request = Assert.Single(fixture.Llm.Requests);
        Assert.DoesNotContain(NoData.PlaceId.ToString(), request.Input);
        Assert.DoesNotContain(NoData.PlaceId.ToString(), request.Schema.ToJsonString());
    }

    [Fact]
    public async Task EveryStopLacksData_SkipsTheModelEntirely_AndStillWritesTheFixedSentence()
    {
        var fixture = new Fixture(Trip(NoData));

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.Success, result.Status);
        Assert.Equal(TripExplanationPrompt.InsufficientDataReason, Assert.Single(result.Response!.Items).Reasoning);
        Assert.Empty(fixture.Llm.Requests);
        Assert.Empty(fixture.Guard.Records);
    }

    [Fact]
    public async Task ModelSkipsAStop_OthersAreStillWritten()
    {
        var fixture = new Fixture(Trip(Cafe, Park));
        fixture.Llm.Answer = Answer((Cafe, true, "Quán yên tĩnh."));

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.Success, result.Status);
        Assert.Equal([Cafe.ItemId], result.Response!.Items.Select(item => item.ItemId));
        Assert.Equal(LlmCallOutcome.Succeeded, Assert.Single(fixture.Guard.Records).Outcome);
    }

    [Fact]
    public async Task ModelFlagsNotEnoughData_StopGetsTheFixedSentence()
    {
        var fixture = new Fixture(Trip(Cafe, Park));
        fixture.Llm.Answer = Answer((Cafe, true, "Quán yên tĩnh."), (Park, false, ""));

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(TripExplanationPrompt.InsufficientDataReason, result.Response!.Items[1].Reasoning);
    }

    [Fact]
    public async Task StopChangedWhileWaiting_IsLeftOutOfTheResponse()
    {
        var fixture = new Fixture(Trip(Cafe, Park));
        fixture.Llm.Answer = Answer((Cafe, true, "Quán yên tĩnh."), (Park, true, "Thoáng mát."));
        // Người dùng vừa thay địa điểm của chặng công viên: repository không ghi chặng đó.
        fixture.Repository.RejectedItemIds.Add(Park.ItemId);

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal([Cafe.ItemId], result.Response!.Items.Select(item => item.ItemId));
    }

    [Fact]
    public async Task TripFinalizedWhileWaiting_ReturnsFinalized_ButTheCallIsStillLogged()
    {
        var fixture = new Fixture(Trip(Cafe));
        fixture.Llm.Answer = Answer((Cafe, true, "Quán yên tĩnh."));
        fixture.Repository.NoLongerEditable = true;

        var result = await fixture.Service.ExplainAsync(UserId, TripId);

        Assert.Equal(ExplainTripResultStatus.TripFinalized, result.Status);
        Assert.Single(fixture.Guard.Records);
    }

    private static TripExplanationReadModel Trip(params TripExplanationStopReadModel[] stops) =>
        new(TripId, TripStatus.Draft, 4, "muốn chỗ yên tĩnh", ["Cà phê"], stops);

    private static TripExplanationStopReadModel Stop(string name, string? description, string[] tags) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, PlaceCategory.Cafe, description, tags, new TimeOnly(14, 0), 60);

    private static string Answer(params (TripExplanationStopReadModel Stop, bool HasEnoughData, string Reason)[] stops) =>
        JsonSerializer.Serialize(new
        {
            stops = stops.Select(entry => new
            {
                placeId = entry.Stop.PlaceId.ToString(),
                hasEnoughData = entry.HasEnoughData,
                reason = entry.Reason
            })
        });

    private sealed class Fixture
    {
        public Fixture(TripExplanationReadModel? trip, bool userExists = true)
        {
            Repository = new FakeRepository(trip);
            Service = new TripExplanationService(
                new FakeUserRepository(userExists ? UserId : null),
                Repository,
                Guard,
                Llm,
                new FixedTimeProvider(Now));
        }

        public FakeRepository Repository { get; }
        public FakeGuard Guard { get; } = new();
        public FakeLlm Llm { get; } = new();
        public TripExplanationService Service { get; }
    }

    private sealed class FakeLlm : ILlmClient
    {
        public string Model => "model-x";
        public bool IsConfigured => true;
        public string Answer { get; set; } = "{\"stops\":[]}";
        public Exception? Failure { get; set; }
        public Action? BeforeAnswer { get; set; }
        public List<LlmJsonRequest> Requests { get; } = [];

        public Task<LlmJsonResponse> GenerateJsonAsync(LlmJsonRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            BeforeAnswer?.Invoke();

            return Failure is null
                ? Task.FromResult(new LlmJsonResponse(Answer, 1100, 420))
                : Task.FromException<LlmJsonResponse>(Failure);
        }
    }

    private sealed class FakeGuard : IAiUsageGuard
    {
        public AiUsageDecision Decision { get; set; } = new(AiUsageStatus.Allowed);
        public List<(Guid UserId, LlmCallKind Kind, Guid? TripId)> Checks { get; } = [];
        public List<(Guid UserId, LlmCallKind Kind, Guid? TripId, LlmCallOutcome Outcome, LlmJsonResponse? Response)> Records { get; } = [];

        public Task<AiUsageDecision> CheckAsync(
            Guid userId, LlmCallKind kind, Guid? tripId = null, CancellationToken cancellationToken = default)
        {
            Checks.Add((userId, kind, tripId));
            return Task.FromResult(Decision);
        }

        public Task RecordAsync(
            Guid userId, LlmCallKind kind, Guid? tripId, LlmCallOutcome outcome, LlmJsonResponse? response,
            int durationMilliseconds, CancellationToken cancellationToken = default)
        {
            Records.Add((userId, kind, tripId, outcome, response));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository(TripExplanationReadModel? trip) : ITripExplanationRepository
    {
        public int Reads { get; private set; }
        public bool NoLongerEditable { get; set; }
        public HashSet<Guid> RejectedItemIds { get; } = [];
        public List<(IReadOnlyList<TripExplanationUpdate> Updates, DateTime ExplainedAt)> Applied { get; } = [];

        public Task<TripExplanationReadModel?> GetOwnedAsync(
            Guid tripId, Guid userId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(trip is not null && tripId == trip.TripId && userId == UserId ? trip : null);
        }

        public Task<IReadOnlyList<TripExplanationUpdate>?> ApplyAsync(
            Guid tripId, Guid userId, IReadOnlyList<TripExplanationUpdate> updates, DateTime explainedAt,
            CancellationToken cancellationToken = default)
        {
            if (NoLongerEditable)
            {
                return Task.FromResult<IReadOnlyList<TripExplanationUpdate>?>(null);
            }

            Applied.Add((updates, explainedAt));
            return Task.FromResult<IReadOnlyList<TripExplanationUpdate>?>(
                updates.Where(update => !RejectedItemIds.Contains(update.ItemId)).ToList());
        }
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
