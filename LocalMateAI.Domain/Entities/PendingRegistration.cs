using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

// Đăng ký đang chờ nhập OTP. Chỉ khi nhập đúng mã mới tạo User thật.
public sealed class PendingRegistration : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    // Mật khẩu đã băm bằng IPasswordHashService, không lưu mật khẩu gốc.
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}
