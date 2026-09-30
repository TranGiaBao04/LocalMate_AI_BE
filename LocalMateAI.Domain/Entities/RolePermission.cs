namespace LocalMateAI.Domain.Entities;

// BE-82: một quyền của một role. Permission là mã trong Application/Security/Permissions (kiểm tra khi lưu).
public sealed class RolePermission
{
    public Guid RoleId { get; set; }

    public string Permission { get; set; } = string.Empty;
}
