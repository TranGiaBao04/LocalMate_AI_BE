namespace LocalMateAI.Application.DTOs.Email;

// Mail cần xếp vào outbox. ModelJson là dữ liệu template đã serialize.
public sealed record EmailOutboxEntry(
    string ToEmail,
    string Subject,
    string TemplateName,
    string ModelJson,
    string DeduplicationKey);
