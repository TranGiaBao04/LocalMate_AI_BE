namespace LocalMateAI.API.Authorization;

public static class AppPolicies
{
    /// <summary>API của người dùng: chỉ cần đăng nhập, role nào cũng dùng được (kể cả role admin tự tạo).</summary>
    public const string RegisteredUser = nameof(RegisteredUser);

    private const string PermissionPrefix = "perm:";

    public static string ForPermission(string permission) => PermissionPrefix + permission;
}
