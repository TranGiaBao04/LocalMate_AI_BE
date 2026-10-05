using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class AiUsageGuard(
    ILlmClient llmClient,
    ILlmCallLogRepository repository,
    ISystemSettingProvider settings,
    TimeProvider timeProvider) : IAiUsageGuard
{
    public async Task<AiUsageDecision> CheckAsync(
        Guid userId,
        LlmCallKind kind,
        Guid? tripId = null,
        CancellationToken cancellationToken = default)
    {
        if (!llmClient.IsConfigured
            || await settings.GetIntAsync(SystemSettingKeys.AiEnabled, cancellationToken) == 0)
        {
            return new AiUsageDecision(AiUsageStatus.Disabled);
        }

        if (tripId is { } id && kind == LlmCallKind.Explain)
        {
            var perTrip = await settings.GetIntAsync(SystemSettingKeys.AiExplainCallsPerTrip, cancellationToken);
            // Chỉ đếm lần thành công: lần bị nhà cung cấp từ chối không làm mất lượt của chuyến đi.
            if (await repository.CountSucceededForTripAsync(id, kind, cancellationToken) >= perTrip)
            {
                return new AiUsageDecision(AiUsageStatus.TripLimitReached);
            }
        }

        // "Ngày" tính theo lịch Việt Nam: trần đặt lại lúc 00:00 giờ Việt Nam.
        var dayStartUtc = VietnamTime.StartOfTodayUtc(timeProvider);
        var perDay = await settings.GetIntAsync(SystemSettingKeys.AiDailyCallsPerUser, cancellationToken);

        return await repository.CountForUserSinceAsync(userId, dayStartUtc, cancellationToken) >= perDay
            ? new AiUsageDecision(AiUsageStatus.DailyLimitReached, dayStartUtc.AddDays(1))
            : new AiUsageDecision(AiUsageStatus.Allowed);
    }

    public Task RecordAsync(
        Guid userId,
        LlmCallKind kind,
        Guid? tripId,
        LlmCallOutcome outcome,
        LlmJsonResponse? response,
        int durationMilliseconds,
        CancellationToken cancellationToken = default) =>
        repository.AddAsync(
            new LlmCallLog
            {
                UserId = userId,
                TripId = tripId,
                Kind = kind,
                Model = llmClient.Model,
                InputTokens = response?.InputTokens ?? 0,
                OutputTokens = response?.OutputTokens ?? 0,
                DurationMilliseconds = durationMilliseconds,
                Outcome = outcome
            },
            cancellationToken);
}
