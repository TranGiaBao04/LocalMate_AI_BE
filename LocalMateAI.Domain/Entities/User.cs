using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    // Bắt buộc gán khi tạo user (lấy Id role User qua ISystemRoleProvider); để trống ⇒ lỗi khoá ngoại khi lưu.
    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTime? LockedAt { get; set; }

    // Ghi chú nội bộ của admin khi khoá, không trả ra cho user.
    public string? LockReason { get; set; }

    // BE-134: admin đã khoá tài khoản (thay audit log đã bỏ). Null khi chưa khoá, hoặc người khoá đã bị xoá.
    public Guid? LockedByUserId { get; set; }

    public ICollection<UserPreferenceTag> PreferenceTags { get; set; } = [];

    // Tên role để hiển thị. User đọc từ DB phải kèm Include(user => user.Role); thiếu thì ném lỗi để lộ bug sớm.
    public string RoleName => Role?.Name
        ?? throw new InvalidOperationException("Role của user chưa được nạp (thiếu Include(user => user.Role)).");
}
