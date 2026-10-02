namespace LocalMateAI.Application.Security;

/// <summary>Tên khoá tuần tự cho IAdminOperationExecutor. Thao tác cùng tên không chạy chồng nhau.</summary>
public static class AdminLocks
{
    // Mọi thao tác đổi role/quyền/gán role (và khoá user ở BE-134) dùng chung khoá này,
    // để luật "luôn còn ≥ 1 người quản lý role đang hoạt động" không bị 2 request cùng lúc phá vỡ.
    public const string RoleManagement = "admin:role-management";
}
