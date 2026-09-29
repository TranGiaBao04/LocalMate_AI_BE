namespace LocalMateAI.Application.DTOs.Email;

// Mail job vừa nhận để gửi. AttemptCount đã tính cả lần gửi hiện tại.
public sealed record ClaimedEmailOutboxMessage(
    Guid Id,
    string ToEmail,
    string Subject,
    string TemplateName,
    string ModelJson,
    int AttemptCount);
