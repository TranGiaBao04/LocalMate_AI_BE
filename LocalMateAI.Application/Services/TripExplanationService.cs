using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripExplanationService(
    IUserRepository userRepository,
    ITripExplanationRepository repository,
    IAiUsageGuard usageGuard,
    ILlmClient llmClient,
    TimeProvider timeProvider) : ITripExplanationService
{
    public static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    public async Task<ExplainTripResult> ExplainAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            return new ExplainTripResult(ExplainTripResultStatus.InvalidId);
        }

        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
        {
            return new ExplainTripResult(ExplainTripResultStatus.UserNotFound);
        }

        var trip = await repository.GetOwnedAsync(tripId, userId, cancellationToken);
        if (trip is null)
        {
            return new ExplainTripResult(ExplainTripResultStatus.TripNotFound);
        }

        if (trip.Status != TripStatus.Draft)
        {
            return new ExplainTripResult(ExplainTripResultStatus.TripFinalized);
        }

        // Kiểm tra trần TRƯỚC khi gọi AI: lần nào đã gửi đi cũng tính vào trần ngày.
        var usage = await usageGuard.CheckAsync(userId, LlmCallKind.Explain, tripId, cancellationToken);
        switch (usage.Status)
        {
            case AiUsageStatus.Disabled:
                return new ExplainTripResult(ExplainTripResultStatus.AiUnavailable);
            case AiUsageStatus.TripLimitReached:
                return new ExplainTripResult(ExplainTripResultStatus.TripLimitReached);
            case AiUsageStatus.DailyLimitReached:
                return new ExplainTripResult(ExplainTripResultStatus.DailyLimitReached, ResetAtUtc: usage.ResetAtUtc);
        }

        // Chặng không có mô tả lẫn tag nhận luôn câu cố định, không gửi cho AI.
        var describable = trip.Stops.Where(TripExplanationPrompt.HasDescribableData).ToList();
        var reasons = trip.Stops
            .Where(stop => !TripExplanationPrompt.HasDescribableData(stop))
            .Select(stop => stop.PlaceId)
            .Distinct()
            .ToDictionary(placeId => placeId, _ => TripExplanationPrompt.InsufficientDataReason);

        if (describable.Count > 0)
        {
            var generated = await GenerateAsync(userId, trip, describable, cancellationToken);
            if (generated is null)
            {
                return new ExplainTripResult(ExplainTripResultStatus.AiUnavailable);
            }

            foreach (var (placeId, reason) in generated)
            {
                reasons[placeId] = reason;
            }
        }

        var explainedAt = timeProvider.GetUtcNow().UtcDateTime;
        var updates = trip.Stops
            .Where(stop => reasons.ContainsKey(stop.PlaceId))
            .Select(stop => new TripExplanationUpdate(stop.ItemId, stop.PlaceId, reasons[stop.PlaceId]))
            .ToList();

        var written = await repository.ApplyAsync(tripId, userId, updates, explainedAt, cancellationToken);
        if (written is null)
        {
            // Trip vừa bị chốt hoặc xoá trong lúc chờ AI.
            return new ExplainTripResult(ExplainTripResultStatus.TripFinalized);
        }

        return new ExplainTripResult(
            ExplainTripResultStatus.Success,
            new ExplainTripResponse(
                written
                    .Select(update => new ExplainedItemResponse(update.ItemId, update.PlaceId, update.Reasoning))
                    .ToList(),
                explainedAt));
    }

    // Trả null khi AI không dùng được hoặc trả lời không có lý do hợp lệ nào; lần gọi nào cũng được ghi log.
    private async Task<IReadOnlyDictionary<Guid, string>?> GenerateAsync(
        Guid userId,
        TripExplanationReadModel trip,
        IReadOnlyList<TripExplanationStopReadModel> stops,
        CancellationToken cancellationToken)
    {
        var placeIds = stops.Select(stop => stop.PlaceId).Distinct().ToList();
        var request = new LlmJsonRequest(
            TripExplanationPrompt.SystemInstruction,
            TripExplanationPrompt.BuildInput(trip, stops),
            TripExplanationPrompt.BuildSchema(placeIds),
            TripExplanationPrompt.MaxOutputTokens);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ProviderTimeout);
        var startedAt = timeProvider.GetTimestamp();

        LlmJsonResponse? response = null;
        try
        {
            response = await llmClient.GenerateJsonAsync(request, timeoutSource.Token);
        }
        catch (Exception exception) when (
            exception is LlmUnavailableException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Lỗi hoặc quá giờ: vẫn ghi log vì yêu cầu đã được gửi đi (tính vào trần ngày).
        }

        var elapsed = (int)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
        var reasons = response is null ? null : TripExplanationOutputParser.Parse(response.Json, placeIds);
        var outcome = response is null
            ? LlmCallOutcome.ProviderFailed
            : reasons is { Count: > 0 }
                ? LlmCallOutcome.Succeeded
                : LlmCallOutcome.InvalidOutput;

        await usageGuard.RecordAsync(
            userId, LlmCallKind.Explain, trip.TripId, outcome, response, elapsed, CancellationToken.None);

        return outcome == LlmCallOutcome.Succeeded ? reasons : null;
    }
}
