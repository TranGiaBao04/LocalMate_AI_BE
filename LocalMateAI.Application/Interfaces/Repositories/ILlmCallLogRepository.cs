using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ILlmCallLogRepository
{
    Task AddAsync(LlmCallLog log, CancellationToken cancellationToken = default);

    /// <summary>Số lần gọi của một người dùng từ thời điểm đã cho (mọi loại, kể cả lần lỗi).</summary>
    Task<int> CountForUserSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Số lần gọi THÀNH CÔNG một loại cho một chuyến đi. Lần bị nhà cung cấp từ chối hoặc trả lời hỏng
    /// không tính, để người dùng không mất lượt của chuyến đi vì lỗi không phải của họ.
    /// </summary>
    Task<int> CountSucceededForTripAsync(Guid tripId, LlmCallKind kind, CancellationToken cancellationToken = default);
}
