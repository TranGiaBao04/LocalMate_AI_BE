using LocalMateAI.Application.DTOs.Email;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IEmailOutboxRepository
{
    // Xếp mail vào outbox, chạy trong transaction hiện tại (nếu có). False nếu DeduplicationKey đã tồn tại.
    Task<bool> EnqueueAsync(
        EmailOutboxEntry entry,
        DateTime now,
        CancellationToken cancellationToken = default);

    // Nhận tối đa batchSize mail đến hạn: tăng AttemptCount và giữ chỗ tới leaseUntil.
    // Nhiều instance gọi cùng lúc không nhận trùng mail (FOR UPDATE SKIP LOCKED).
    Task<IReadOnlyList<ClaimedEmailOutboxMessage>> ClaimDueAsync(
        DateTime now,
        DateTime leaseUntil,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task MarkSentAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    Task ScheduleRetryAsync(
        Guid id,
        DateTime nextAttemptAt,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid id, DateTime now, CancellationToken cancellationToken = default);

    // Xoá mail đã gửi trước sentBefore và mail gửi lỗi hẳn trước failedBefore. Trả số dòng đã xoá.
    Task<int> DeleteExpiredAsync(
        DateTime sentBefore,
        DateTime failedBefore,
        CancellationToken cancellationToken = default);
}
