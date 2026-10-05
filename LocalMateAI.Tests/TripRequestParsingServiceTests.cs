using System.Text.Json;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripRequestParsingServiceTests
{
    // 2026-10-05 08:30 UTC = 15:30 Thứ Hai 05/10 giờ Việt Nam.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 30, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Tag Coffee = new() { Name = "Cà phê", Type = TagType.Interest };
    private static readonly Tag Food = new() { Name = "Ẩm thực", Type = TagType.Interest };

    [Fact]
    public async Task Success_ReturnsFieldsAndWhatIsStillMissing_AndLogsTheCall()
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = Answer(new
        {
            isTripRequest = true,
            durationHours = 4,
            budgetMax = 300000,
            tags = new[] { "Cà phê" },
            destinationStationOrder = 1,
            plannedDate = "2026-10-06",
            startTime = "13:00"
        });

        var result = await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("  chiều mai   đi cà phê quanh Bến Thành "));

        Assert.Equal(ParseTripRequestResultStatus.Success, result.Status);
        var response = result.Response!;
        Assert.True(response.IsTripRequest);
        Assert.Null(response.Message);
        Assert.Equal(4, response.Fields.DurationHours);
        Assert.Equal([Coffee.Id], response.Fields.TagIds);
        Assert.Equal(1, response.Fields.DestinationStationOrder);
        Assert.Equal(["startLocation"], response.Missing);

        var request = Assert.Single(fixture.Llm.Requests);
        Assert.Equal(TripRequestParsingPrompt.SystemInstruction, request.SystemInstruction);
        Assert.Equal(TripRequestParsingPrompt.Temperature, request.Temperature);
        Assert.Contains("chiều mai đi cà phê quanh Bến Thành", request.Input); // đã gộp khoảng trắng
        Assert.Contains("\"today\":\"2026-10-05\"", request.Input);
        Assert.DoesNotContain(UserId.ToString(), request.Input);

        var record = Assert.Single(fixture.Guard.Records);
        Assert.Equal((UserId, LlmCallKind.ParseRequest, (Guid?)null, LlmCallOutcome.Succeeded), (record.UserId, record.Kind, record.TripId, record.Outcome));
        Assert.Equal((UserId, LlmCallKind.ParseRequest, (Guid?)null), Assert.Single(fixture.Guard.Checks));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  đi  ")]
    [InlineData("a b")]
    public async Task TextTooShort_IsInvalid_WithoutAnyLookupOrModelCall(string? text)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest(text));

        Assert.Equal(ParseTripRequestResultStatus.InvalidText, result.Status);
        Assert.Equal(["Text"], result.ValidationErrors!.Keys);
        Assert.Empty(fixture.Guard.Checks);
        Assert.Empty(fixture.Llm.Requests);
    }

    [Fact]
    public async Task TextLength_IsCountedAfterCollapsingWhitespace()
    {
        var fixture = new Fixture();
        var exactlyMax = new string('a', TripRequestParsingPrompt.MaxTextLength);

        Assert.Equal(
            ParseTripRequestResultStatus.Success,
            (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest($"   {exactlyMax}   "))).Status);
        Assert.Equal(
            ParseTripRequestResultStatus.InvalidText,
            (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest(exactlyMax + "a"))).Status);
    }

    [Fact]
    public async Task NonPersistedUser_IsRejectedBeforeCheckingUsage()
    {
        var fixture = new Fixture(userExists: false);

        var result = await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("muốn đi chơi chiều nay"));

        Assert.Equal(ParseTripRequestResultStatus.UserNotFound, result.Status);
        Assert.Empty(fixture.Guard.Checks);
    }

    [Theory]
    [InlineData(AiUsageStatus.Disabled, ParseTripRequestResultStatus.AiUnavailable)]
    [InlineData(AiUsageStatus.DailyLimitReached, ParseTripRequestResultStatus.DailyLimitReached)]
    public async Task UsageNotAllowed_StopsBeforeCallingTheModel(AiUsageStatus usage, ParseTripRequestResultStatus expected)
    {
        var resetAt = new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc);
        var fixture = new Fixture();
        fixture.Guard.Decision = new AiUsageDecision(usage, usage == AiUsageStatus.DailyLimitReached ? resetAt : null);

        var result = await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("muốn đi chơi chiều nay"));

        Assert.Equal(expected, result.Status);
        Assert.Equal(usage == AiUsageStatus.DailyLimitReached ? resetAt : null, result.ResetAtUtc);
        Assert.Empty(fixture.Llm.Requests);
        Assert.Empty(fixture.Guard.Records);
    }

    [Fact]
    public async Task ProviderFailureOrTimeout_ReturnsUnavailable_AndLogsTheAttempt()
    {
        var failed = new Fixture();
        failed.Llm.Failure = new LlmUnavailableException("high demand");
        var timedOut = new Fixture();
        timedOut.Llm.Failure = new OperationCanceledException("timed out");

        Assert.Equal(
            ParseTripRequestResultStatus.AiUnavailable,
            (await failed.Service.ParseAsync(UserId, new ParseTripRequestRequest("muốn đi chơi chiều nay"))).Status);
        Assert.Equal(
            ParseTripRequestResultStatus.AiUnavailable,
            (await timedOut.Service.ParseAsync(UserId, new ParseTripRequestRequest("muốn đi chơi chiều nay"))).Status);

        Assert.Equal(LlmCallOutcome.ProviderFailed, Assert.Single(failed.Guard.Records).Outcome);
        Assert.Equal(LlmCallOutcome.ProviderFailed, Assert.Single(timedOut.Guard.Records).Outcome);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_WithoutLogging()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        fixture.Llm.BeforeAnswer = cancellation.Cancel;
        fixture.Llm.Failure = new OperationCanceledException("cancelled by caller");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ParseAsync(
            UserId, new ParseTripRequestRequest("muốn đi chơi chiều nay"), cancellation.Token));

        Assert.Empty(fixture.Guard.Records);
    }

    [Fact]
    public async Task MalformedAnswer_ReturnsUnavailable_AndIsLoggedAsInvalidOutput()
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = "not json";

        var result = await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("muốn đi chơi chiều nay"));

        Assert.Equal(ParseTripRequestResultStatus.AiUnavailable, result.Status);
        Assert.Equal(LlmCallOutcome.InvalidOutput, Assert.Single(fixture.Guard.Records).Outcome);
    }

    [Fact]
    public async Task NotATripRequest_ReturnsThePoliteMessage_AndNoMissingList()
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = Answer(new { isTripRequest = false });

        var response = (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("hôm nay tôi buồn"))).Response!;

        Assert.False(response.IsTripRequest);
        Assert.Equal(TripRequestParsingPrompt.NotATripRequestMessage, response.Message);
        Assert.False(response.Fields.HasAnyValue);
        Assert.Empty(response.Missing);
    }

    [Fact]
    public async Task WantsToGoOutButGaveNoCriteria_StaysATripRequest_AndInvitesMoreDetail()
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = Answer(new { isTripRequest = true, tags = Array.Empty<string>() });

        var response = (await fixture.Service.ParseAsync(
            UserId, new ParseTripRequestRequest("hôm nay tôi đang buồn muốn đi đâu đó"))).Response!;

        Assert.True(response.IsTripRequest);
        Assert.Equal(TripRequestParsingPrompt.NeedsMoreDetailMessage, response.Message);
        Assert.Equal(["durationHours", "budgetMax", "startLocation"], response.Missing);
    }

    [Fact]
    public async Task OnlyDateTimeOrTransport_KeepsThoseFields_ButStillInvitesMoreDetail()
    {
        var fixture = new Fixture();
        // "hôm nay tôi đang buồn muốn đi đâu đó": AI chỉ lấy được ngày đi.
        fixture.Llm.Answer = Answer(new { isTripRequest = true, plannedDate = "2026-10-05", travelMode = "Walking" });

        var response = (await fixture.Service.ParseAsync(
            UserId, new ParseTripRequestRequest("hôm nay tôi đang buồn muốn đi đâu đó"))).Response!;

        Assert.True(response.IsTripRequest);
        Assert.Equal(new DateOnly(2026, 10, 5), response.Fields.PlannedDate);
        Assert.Equal(TravelMode.Walking, response.Fields.TravelMode);
        Assert.Equal(TripRequestParsingPrompt.NeedsMoreDetailMessage, response.Message);
    }

    [Theory]
    [InlineData("{\"isTripRequest\":true,\"durationHours\":3}")]
    [InlineData("{\"isTripRequest\":true,\"budgetMax\":200000}")]
    [InlineData("{\"isTripRequest\":true,\"tags\":[\"Cà phê\"]}")]
    [InlineData("{\"isTripRequest\":true,\"note\":\"đi dạo\"}")]
    [InlineData("{\"isTripRequest\":true,\"destinationStationOrder\":3}")]
    public async Task AnyRealCriterion_MeansNoMessage(string answer)
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = answer;

        var response = (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("muốn đi chơi chiều nay"))).Response!;

        Assert.Null(response.Message);
    }

    [Fact]
    public void Response_DoesNotExposeInternalHelperFlags()
    {
        var json = JsonSerializer.Serialize(
            new ParseTripRequestResponse(true, null, ParsedTripFields.Empty with { DurationHours = 3 }, [], []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"durationHours\":3", json);
        Assert.DoesNotContain("hasAnyValue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hasTripCriteria", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryFieldRejectedByValidation_IsTreatedLikeNoCriteria()
    {
        var fixture = new Fixture();
        // AI trả toàn giá trị sai luật: 30 tiếng, ngày năm 2020, tag không có.
        fixture.Llm.Answer = Answer(new
        {
            isTripRequest = true,
            durationHours = 30,
            plannedDate = "2020-01-01",
            tags = new[] { "Leo núi" }
        });

        var response = (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("đi chơi 30 tiếng ngày 1/1/2020"))).Response!;

        Assert.True(response.IsTripRequest);
        Assert.False(response.Fields.HasAnyValue);
        Assert.Equal(TripRequestParsingPrompt.NeedsMoreDetailMessage, response.Message);
    }

    [Fact]
    public async Task OnlyActiveTagNames_AreOfferedToTheModel()
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = Answer(new { isTripRequest = true, tags = new[] { "Ẩm thực" } });

        var response = (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("tôi đang đói bụng"))).Response!;

        Assert.Equal([Food.Id], response.Fields.TagIds);
        var schema = Assert.Single(fixture.Llm.Requests).Schema.ToJsonString();
        using var document = JsonDocument.Parse(schema);
        Assert.Equal(
            ["Cà phê", "Ẩm thực"],
            document.RootElement.GetProperty("properties").GetProperty("tags").GetProperty("items")
                .GetProperty("enum").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Fact]
    public async Task WithBase_ReturnsMergedFields_ChangedList_AndSendsTheBaseToTheModel()
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = Answer(new { isTripRequest = true, budgetMax = 350000 });

        var response = (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("rẻ hơn chút", ExistingTrip))).Response!;

        Assert.True(response.IsTripRequest);
        Assert.Null(response.Message);
        Assert.Equal(350_000m, response.Fields.BudgetMax);
        Assert.Equal(5, response.Fields.DurationHours); // giữ của lịch gốc
        Assert.Equal([Coffee.Id], response.Fields.TagIds);
        Assert.Equal(["budgetMax"], response.Changed);
        Assert.Equal(["startLocation"], response.Missing);

        using var input = JsonDocument.Parse(Assert.Single(fixture.Llm.Requests).Input);
        var sent = input.RootElement.GetProperty("base");
        Assert.Equal(500000, sent.GetProperty("budgetMax").GetInt32());
        Assert.Equal(["Cà phê"], sent.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
    }

    [Theory]
    [InlineData("{\"isTripRequest\":false}", false)]
    [InlineData("{\"isTripRequest\":true}", true)]
    [InlineData("{\"isTripRequest\":true,\"durationHours\":5,\"budgetMax\":500000}", true)]
    public async Task WithBase_NothingChanged_KeepsTheTripCriteria_AndSaysSo(string answer, bool isTripRequest)
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = answer;

        var response = (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("hôm nay tôi buồn", ExistingTrip))).Response!;

        Assert.Equal(isTripRequest, response.IsTripRequest);
        Assert.Equal(TripRequestParsingPrompt.NoChangeMessage, response.Message);
        Assert.Empty(response.Changed);
        Assert.Equal(500_000m, response.Fields.BudgetMax);
        Assert.Equal(["startLocation"], response.Missing);
    }

    [Fact]
    public async Task WithBase_OnlyValidBaseValues_AreSentToTheModel()
    {
        var fixture = new Fixture();
        // Lịch cũ: ngày đi đã qua và có một tag không còn bật.
        var oldTrip = ExistingTrip with { PlannedDate = new DateOnly(2026, 9, 1), TagIds = [Coffee.Id, Guid.NewGuid()] };

        await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("rẻ hơn chút", oldTrip));

        using var input = JsonDocument.Parse(Assert.Single(fixture.Llm.Requests).Input);
        var sent = input.RootElement.GetProperty("base");
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("plannedDate").ValueKind);
        Assert.Equal(1, sent.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task WithoutBase_ChangedIsEmpty_AndTheModelGetsNoBase()
    {
        var fixture = new Fixture();
        fixture.Llm.Answer = Answer(new { isTripRequest = true, durationHours = 3 });

        var response = (await fixture.Service.ParseAsync(UserId, new ParseTripRequestRequest("đi chơi 3 tiếng"))).Response!;

        Assert.Empty(response.Changed);
        using var input = JsonDocument.Parse(Assert.Single(fixture.Llm.Requests).Input);
        Assert.False(input.RootElement.TryGetProperty("base", out _));
    }

    // Lịch đang xem: 5 giờ, 500.000đ, tag Cà phê, quanh ga 2, ngày mai 09:00.
    private static ParsedTripFields ExistingTrip => new(
        5, 500_000m, [Coffee.Id], TravelMode.Auto, null, 2, new DateOnly(2026, 10, 6), new TimeOnly(9, 0), "yên tĩnh");

    private static string Answer(object answer) => JsonSerializer.Serialize(answer);

    private sealed class Fixture
    {
        public Fixture(bool userExists = true) =>
            Service = new TripRequestParsingService(
                new FakeUserRepository(userExists ? UserId : null),
                new FakeTagRepository(),
                new FakeMasterDataService(),
                Guard,
                Llm,
                new FixedTimeProvider(Now));

        public FakeGuard Guard { get; } = new();
        public FakeLlm Llm { get; } = new();
        public TripRequestParsingService Service { get; }
    }

    private sealed class FakeLlm : ILlmClient
    {
        public string Model => "model-x";
        public bool IsConfigured => true;
        public string Answer { get; set; } = "{\"isTripRequest\":true,\"tags\":[]}";
        public Exception? Failure { get; set; }
        public Action? BeforeAnswer { get; set; }
        public List<LlmJsonRequest> Requests { get; } = [];

        public Task<LlmJsonResponse> GenerateJsonAsync(LlmJsonRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            BeforeAnswer?.Invoke();

            return Failure is null
                ? Task.FromResult(new LlmJsonResponse(Answer, 1100, 90))
                : Task.FromException<LlmJsonResponse>(Failure);
        }
    }

    private sealed class FakeGuard : IAiUsageGuard
    {
        public AiUsageDecision Decision { get; set; } = new(AiUsageStatus.Allowed);
        public List<(Guid UserId, LlmCallKind Kind, Guid? TripId)> Checks { get; } = [];
        public List<(Guid UserId, LlmCallKind Kind, Guid? TripId, LlmCallOutcome Outcome)> Records { get; } = [];

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
            Records.Add((userId, kind, tripId, outcome));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTagRepository : ITagRepository
    {
        public Task<IReadOnlyList<Tag>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tag>>([Food, Coffee]);

        public Task<IReadOnlyList<Tag>> GetByIdsAsync(
            IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMasterDataService : IMasterDataService
    {
        public Task<MasterDataResponse> GetMasterDataAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MasterDataResponse(
                Enumerable.Range(1, 14)
                    .Select(order => new MetroStationSummaryResponse(Guid.NewGuid(), $"Ga {order}", order, 0, 0))
                    .ToList(),
                [], [], [], [],
                new TripLimitsResponse(1, 24),
                [
                    new TimeSlotResponse("morning", "Buổi sáng", new TimeOnly(8, 0), 16),
                    new TimeSlotResponse("afternoon", "Buổi chiều", new TimeOnly(13, 0), 11),
                    new TimeSlotResponse("evening", "Buổi tối", new TimeOnly(18, 0), 6)
                ]));
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
