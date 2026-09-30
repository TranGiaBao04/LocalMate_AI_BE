using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Security;

/// <summary>BE-82: nơi DUY NHẤT tính quyền thực tế của một role.</summary>
public static class RolePermissionRules
{
    private static readonly string AdminNormalizedName = SystemRoles.Normalize(SystemRoles.AdminName);

    /// <summary>
    /// Role hệ thống Admin ⇒ mọi quyền (kể cả quyền thêm sau). Role khác ⇒ quyền đã lưu, bỏ mã không còn trong code.
    /// Kết quả theo thứ tự của <see cref="Permissions.All"/>.
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        string normalizedRoleName,
        bool isSystemRole,
        IEnumerable<string> storedPermissions)
    {
        if (isSystemRole && normalizedRoleName == AdminNormalizedName)
        {
            return Permissions.All.Select(permission => permission.Code).ToArray();
        }

        var stored = storedPermissions.ToHashSet(StringComparer.Ordinal);
        return Permissions.All
            .Where(permission => stored.Contains(permission.Code))
            .Select(permission => permission.Code)
            .ToArray();
    }

    /// <summary>Bản cho entity Role đã nạp kèm Permissions.</summary>
    public static IReadOnlyList<string> Resolve(Role role) =>
        Resolve(role.NormalizedName, role.IsSystem, role.Permissions.Select(permission => permission.Permission));
}
