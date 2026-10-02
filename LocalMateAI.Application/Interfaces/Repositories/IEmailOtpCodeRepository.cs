using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IEmailOtpCodeRepository
{
    // Thời điểm tạo các mã từ "since" tới nay, tăng dần — dùng cho cooldown 60 giây và giới hạn 5 lần/giờ.
    Task<IReadOnlyList<DateTime>> GetCreatedTimesSinceAsync(
        string email,
        OtpPurpose purpose,
        DateTime since,
        CancellationToken cancellationToken = default);

    Task<EmailOtpCode?> GetActiveAsync(
        string email,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default);

    // Vô hiệu mã cũ còn hiệu lực rồi lưu mã mới. False nếu request khác vừa tạo mã cùng lúc.
    Task<bool> TryReplaceActiveAsync(
        EmailOtpCode otpCode,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<bool> TryIncrementAttemptAsync(
        Guid otpCodeId,
        int maxAttempts,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<bool> TryConsumeAsync(
        Guid otpCodeId,
        int maxAttempts,
        DateTime now,
        CancellationToken cancellationToken = default);
}
