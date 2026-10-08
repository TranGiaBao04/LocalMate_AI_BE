using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripRequestParsingService(
    IUserRepository userRepository,
    ITagRepository tagRepository,
    IMasterDataService masterDataService,
    IAiUsageCoordinator usageCoordinator,
    ILlmClient llmClient,
    TimeProvider timeProvider) : ITripRequestParsingService
{
    public static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    public async Task<ParseTripRequestResult> ParseAsync(
        Guid userId,
        ParseTripRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = TripRequestParsingPrompt.NormalizeText(request.Text);
        if (text.Length is < TripRequestParsingPrompt.MinTextLength or > TripRequestParsingPrompt.MaxTextLength)
        {
            return new ParseTripRequestResult(
                ParseTripRequestResultStatus.InvalidText,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    [nameof(ParseTripRequestRequest.Text)] =
                    [
                        $"Hãy mô tả chuyến đi bạn muốn, từ {TripRequestParsingPrompt.MinTextLength} đến "
                        + $"{TripRequestParsingPrompt.MaxTextLength} ký tự."
                    ]
                });
        }

        if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
        {
            return new ParseTripRequestResult(ParseTripRequestResultStatus.UserNotFound);
        }

        var admission = await usageCoordinator.AdmitAsync(userId, LlmCallKind.ParseRequest, null, cancellationToken);
        var usage = admission.Decision;
        if (usage.Status == AiUsageStatus.DailyLimitReached)
        {
            return new ParseTripRequestResult(
                ParseTripRequestResultStatus.DailyLimitReached, ResetAtUtc: usage.ResetAtUtc);
        }

        if (usage.Status != AiUsageStatus.Allowed)
        {
            return new ParseTripRequestResult(ParseTripRequestResultStatus.AiUnavailable);
        }

        await using var call = admission.Call ?? throw new InvalidOperationException("Missing AI admission scope.");
        var tags = await tagRepository.GetActiveAsync(cancellationToken);
        var masterData = await masterDataService.GetMasterDataAsync(cancellationToken);
        var vietnamNow = VietnamTime.Now(timeProvider);
        var tagNames = tags.Select(tag => tag.Name).Distinct().Order(StringComparer.Ordinal).ToList();
        var tagIdsByName = tags
            .GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var stationOrders = masterData.MetroStations.Select(station => station.Order).ToHashSet();

        // Lịch gốc do client gửi lên: BE không tin, chỉ gửi cho AI những gì còn hợp lệ theo luật của form.
        var baseFields = request.Base is null
            ? null
            : TripRequestParsingOutputParser.Clean(request.Base, tagIdsByName.Values.ToHashSet(), stationOrders, vietnamNow);

        var llmRequest = new LlmJsonRequest(
            TripRequestParsingPrompt.SystemInstruction,
            TripRequestParsingPrompt.BuildInput(
                text,
                vietnamNow,
                tagNames,
                masterData.MetroStations,
                masterData.TimeSlots,
                baseFields,
                tags.ToDictionary(tag => tag.Id, tag => tag.Name)),
            TripRequestParsingPrompt.BuildSchema(tagNames),
            TripRequestParsingPrompt.MaxOutputTokens,
            TripRequestParsingPrompt.Temperature);

        await call.AuthorizeAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ProviderTimeout);
        var startedAt = timeProvider.GetTimestamp();

        LlmJsonResponse? response = null;
        try
        {
            response = await llmClient.GenerateJsonAsync(llmRequest, timeoutSource.Token);
        }
        catch (Exception exception) when (
            exception is LlmUnavailableException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Lỗi hoặc quá giờ: vẫn ghi log vì yêu cầu đã được gửi đi (tính vào trần ngày).
        }

        var answer = response is null
            ? null
            : TripRequestParsingOutputParser.Parse(response.Json, tagIdsByName, stationOrders, vietnamNow, baseFields);

        var outcome = response is null
            ? LlmCallOutcome.ProviderFailed
            : answer is null
                ? LlmCallOutcome.InvalidOutput
                : LlmCallOutcome.Succeeded;

        await call.CompleteAsync(
            outcome,
            response,
            (int)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);

        if (answer is null)
        {
            return new ParseTripRequestResult(ParseTripRequestResultStatus.AiUnavailable);
        }

        // Tin AI khi nó xác định người dùng muốn đi chơi, kể cả khi chưa có tiêu chí nào về chuyến đi
        // ("hôm nay buồn muốn đi đâu đó" chỉ cho biết ngày): khi đó mời họ nói thêm thay vì bảo là không hiểu.
        // Đang chỉnh một lịch có sẵn thì chỉ có một câu: chưa thấy đổi gì.
        var hasBase = baseFields is not null;
        var message = hasBase
            ? answer.IsTripRequest && answer.Changed.Count > 0 ? null : TripRequestParsingPrompt.NoChangeMessage
            : !answer.IsTripRequest
                ? TripRequestParsingPrompt.NotATripRequestMessage
                : answer.Fields.HasTripCriteria
                    ? null
                    : TripRequestParsingPrompt.NeedsMoreDetailMessage;

        return new ParseTripRequestResult(
            ParseTripRequestResultStatus.Success,
            new ParseTripRequestResponse(
                answer.IsTripRequest,
                message,
                answer.Fields,
                answer.IsTripRequest || hasBase ? TripRequestParsingOutputParser.Missing(answer.Fields) : [],
                answer.Changed));
    }
}
