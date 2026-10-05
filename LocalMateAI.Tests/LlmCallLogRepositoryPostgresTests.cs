using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class LlmCallLogRepositoryPostgresTests
{
    private static readonly DateTime Since = new(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CountForUserSince_CountsEveryKindAndOutcome_ButNotOlderCallsOrOtherUsers()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var other = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var repository = new LlmCallLogRepository(c);

        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, Since);
        await AddAsync(c, repository, user.Id, LlmCallKind.ParseRequest, LlmCallOutcome.ProviderFailed, Since.AddHours(3));
        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.InvalidOutput, Since.AddHours(5));
        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, Since.AddSeconds(-1));
        await AddAsync(c, repository, other.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, Since.AddHours(1));

        Assert.Equal(3, await repository.CountForUserSinceAsync(user.Id, Since));
        Assert.Equal(1, await repository.CountForUserSinceAsync(other.Id, Since));
        Assert.Equal(0, await repository.CountForUserSinceAsync(Guid.NewGuid(), Since));
    }

    [Fact]
    public async Task CountSucceededForTrip_CountsOnlySuccessfulCallsOfThatTripAndKind_AndLogsSurviveTripRemoval()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var trip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var otherTrip = await PostgresTestDatabase.InsertTripAsync(c, user.Id);
        var repository = new LlmCallLogRepository(c);

        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, Since, trip.Id);
        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, Since, trip.Id);
        // Lần bị nhà cung cấp từ chối và lần trả lời hỏng không làm mất lượt của chuyến đi.
        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.ProviderFailed, Since, trip.Id);
        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.InvalidOutput, Since, trip.Id);
        await AddAsync(c, repository, user.Id, LlmCallKind.ParseRequest, LlmCallOutcome.Succeeded, Since, trip.Id);
        await AddAsync(c, repository, user.Id, LlmCallKind.Explain, LlmCallOutcome.Succeeded, Since, otherTrip.Id);

        Assert.Equal(2, await repository.CountSucceededForTripAsync(trip.Id, LlmCallKind.Explain));
        Assert.Equal(1, await repository.CountSucceededForTripAsync(trip.Id, LlmCallKind.ParseRequest));
        Assert.Equal(1, await repository.CountSucceededForTripAsync(otherTrip.Id, LlmCallKind.Explain));
        // Trần ngày vẫn đếm mọi lần gọi, kể cả lần lỗi.
        Assert.Equal(6, await repository.CountForUserSinceAsync(user.Id, Since));

        // Xoá hẳn chuyến đi: log vẫn còn (vẫn tính vào trần ngày), chỉ mất liên kết tới chuyến đi.
        await c.Trips.Where(candidate => candidate.Id == trip.Id).ExecuteDeleteAsync();

        Assert.Equal(0, await repository.CountSucceededForTripAsync(trip.Id, LlmCallKind.Explain));
        Assert.Equal(6, await repository.CountForUserSinceAsync(user.Id, Since));
    }

    [Fact]
    public async Task Add_StoresEveryField()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);

        await new LlmCallLogRepository(c).AddAsync(new LlmCallLog
        {
            UserId = user.Id,
            Kind = LlmCallKind.Explain,
            Model = "gemini-3.5-flash-lite",
            InputTokens = 1100,
            OutputTokens = 420,
            DurationMilliseconds = 2300,
            Outcome = LlmCallOutcome.InvalidOutput
        });
        c.ChangeTracker.Clear();

        var stored = await c.LlmCallLogs.AsNoTracking().SingleAsync();
        Assert.Equal(user.Id, stored.UserId);
        Assert.Null(stored.TripId);
        Assert.Equal(LlmCallKind.Explain, stored.Kind);
        Assert.Equal("gemini-3.5-flash-lite", stored.Model);
        Assert.Equal(1100, stored.InputTokens);
        Assert.Equal(420, stored.OutputTokens);
        Assert.Equal(2300, stored.DurationMilliseconds);
        Assert.Equal(LlmCallOutcome.InvalidOutput, stored.Outcome);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    // CreatedAt do AppDbContext tự đặt lúc lưu, nên ghi xong mới chỉnh lại thời điểm cho từng ca.
    private static async Task AddAsync(
        AppDbContext context,
        LlmCallLogRepository repository,
        Guid userId,
        LlmCallKind kind,
        LlmCallOutcome outcome,
        DateTime createdAt,
        Guid? tripId = null)
    {
        var log = new LlmCallLog { UserId = userId, TripId = tripId, Kind = kind, Model = "model-x", Outcome = outcome };
        await repository.AddAsync(log);
        await context.LlmCallLogs
            .Where(candidate => candidate.Id == log.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.CreatedAt, createdAt));
        context.ChangeTracker.Clear();
    }
}
