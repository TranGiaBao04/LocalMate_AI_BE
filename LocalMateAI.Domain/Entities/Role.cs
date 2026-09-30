using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

// BE-82: role do admin quản lý. Quyền của role nằm trong RolePermissions (trừ Admin: có mọi quyền theo luật cứng).
public sealed class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    // Name viết hoa (SystemRoles.Normalize), dùng để chống trùng tên không phân biệt hoa thường.
    public string NormalizedName { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Role hệ thống (User, Admin): không đổi tên, không sửa quyền, không xoá.
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = [];
}
