namespace LocalMateAI.Domain.Common;

// BE-82: 2 role hệ thống luôn có trong bảng Roles (migration AddRbacAndUserStatus chèn, Id do DB sinh).
// Nhận ra bằng tên, vì role hệ thống không đổi tên, không xoá được.
// User: mặc định khi đăng ký/đăng nhập Google. Admin: có mọi quyền theo luật cứng, không lưu RolePermissions.
public static class SystemRoles
{
    public const string UserName = "User";
    public const string AdminName = "Admin";

    // Tên chuẩn hoá dùng để so sánh/chống trùng (cột Roles.NormalizedName).
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
