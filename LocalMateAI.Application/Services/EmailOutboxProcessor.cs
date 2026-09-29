using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class EmailOutboxProcessor(
    IEmailOutboxRepository outboxRepository,
    IEmailTemplateRenderer templateRenderer,
    IEmailSender emailSender,
    EmailOutboxModelRegistry modelRegistry,
    TimeProvider timeProvider,
    ILogger<EmailOutboxProcessor> logger) : IEmailOutboxProcessor
{
    public const int BatchSize = 20;
    public const int MaxAttempts = 5;

    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SentRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan FailedRetention = TimeSpan.FromDays(30);

    // Thời gian chờ sau lần thử thứ 1, 2, 3, 4 bị lỗi. Lần thứ 5 lỗi thì đánh dấu Failed.
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60)
    ];

    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var messages = await outboxRepository.ClaimDueAsync(
            now,
            now.Add(LeaseDuration),
            BatchSize,
            cancellationToken);

        foreach (var message in messages)
        {
            await ProcessMessageAsync(message, cancellationToken);
        }

        return messages.Count;
    }

    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        return outboxRepository.DeleteExpiredAsync(
            now.Subtract(SentRetention),
            now.Subtract(FailedRetention),
            cancellationToken);
    }

    private async Task ProcessMessageAsync(
        ClaimedEmailOutboxMessage message,
        CancellationToken cancellationToken)
    {
        string htmlBody;
        try
        {
            var model = modelRegistry.Deserialize(message.TemplateName, message.ModelJson)
                ?? throw new InvalidOperationException($"Email template '{message.TemplateName}' is not registered for the outbox.");
            htmlBody = await templateRenderer.RenderAsync(message.TemplateName, model, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Lỗi template/model không tự hết khi thử lại nên đánh dấu Failed ngay.
            logger.LogError(
                exception,
                "Email outbox message {MessageId} could not be rendered with template {TemplateName}; marked as failed",
                message.Id,
                message.TemplateName);
            await outboxRepository.MarkFailedAsync(message.Id, UtcNow(), CancellationToken.None);
            return;
        }

        // Huỷ giữa lúc gửi (app đang tắt) thì để nguyên: mail tự đến hạn lại khi hết thời gian giữ chỗ.
        var sent = await emailSender.TrySendAsync(
            message.ToEmail,
            message.Subject,
            htmlBody,
            cancellationToken);

        // Đã gửi thì luôn ghi kết quả (không dùng token bị huỷ), tránh gửi trùng lần nữa.
        var now = UtcNow();
        if (sent)
        {
            await outboxRepository.MarkSentAsync(message.Id, now, CancellationToken.None);
            return;
        }

        if (message.AttemptCount >= MaxAttempts)
        {
            logger.LogWarning(
                "Email outbox message {MessageId} failed after {AttemptCount} attempts; marked as failed",
                message.Id,
                message.AttemptCount);
            await outboxRepository.MarkFailedAsync(message.Id, now, CancellationToken.None);
            return;
        }

        var delay = RetryDelays[Math.Clamp(message.AttemptCount - 1, 0, RetryDelays.Count - 1)];
        await outboxRepository.ScheduleRetryAsync(message.Id, now.Add(delay), now, CancellationToken.None);
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
