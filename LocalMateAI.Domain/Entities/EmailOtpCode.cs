using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

// Mã OTP gửi qua email. Gắn theo email (không theo UserId) vì lúc đăng ký chưa có User.
public sealed class EmailOtpCode : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public OtpPurpose Purpose { get; set; }

    // HMAC-SHA256 (hex) của email + mục đích + mã, không lưu mã gốc.
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }

    // Thời điểm mã được dùng hoặc bị thay bằng mã mới. Null = còn hiệu lực.
    public DateTime? ConsumedAt { get; set; }
}
