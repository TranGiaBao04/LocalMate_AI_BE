namespace LocalMateAI.Application.Interfaces.Services;

public interface IEmailOutboxProcessor
{
    // Nhận và gửi 1 lượt mail đến hạn. Trả số mail đã nhận (bằng BatchSize nghĩa là có thể còn mail chờ).
    Task<int> ProcessDueAsync(CancellationToken cancellationToken = default);

    // Xoá mail đã gửi quá 7 ngày và mail gửi lỗi hẳn quá 30 ngày. Trả số dòng đã xoá.
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
