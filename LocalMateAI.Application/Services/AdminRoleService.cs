using LocalMateAI.Application.DTOs.Roles;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>
/// BE-82: quản lý role động. Luật chống tự khoá: role hệ thống không sửa/xoá; không xoá role đang có user;
/// không tự đổi role mình; không bỏ ManageRoles khỏi role mình đang giữ; luôn còn ≥ 1 user Active có ManageRoles.
/// Mọi thao tác ghi chạy dưới khoá AdminLocks.RoleManagement.
/// </summary>
public sealed class AdminRoleService(
    IAdminRoleRepository roleRepository,
    IAdminOperationExecutor operationExecutor) : IAdminRoleService
{
    public const int MaxNameLength = 50;
    public const int MaxDescriptionLength = 200;

    public IReadOnlyList<PermissionResponse> GetPermissions() =>
        Permissions.All
            .Select(permission => new PermissionResponse(permission.Code, permission.Name, permission.Description))
            .ToArray();

    public async Task<IReadOnlyList<AdminRoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        (await roleRepository.GetAllAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<AdminRoleResponse?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetByIdAsync(roleId, cancellationToken);
        return role is null ? null : Map(role);
    }

    public async Task<AdminRoleResult> CreateRoleAsync(
        SaveRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var input = Validate(request, out var errors);
        if (input is null)
        {
            return AdminRoleResult.ValidationFailed(errors);
        }

        return await operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.RoleManagement,
            async operationCancellationToken =>
            {
                if (await roleRepository.NormalizedNameExistsAsync(input.NormalizedName, null, operationCancellationToken))
                {
                    return AdminRoleResult.NameTaken();
                }

                var role = new Role
                {
                    Name = input.Name,
                    NormalizedName = input.NormalizedName,
                    Description = input.Description,
                    IsSystem = false
                };
                foreach (var permission in input.Permissions)
                {
                    role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
                }

                if (!await roleRepository.TryAddAsync(role, operationCancellationToken))
                {
                    return AdminRoleResult.NameTaken();
                }

                return AdminRoleResult.Succeeded(await GetRequiredAsync(role.Id, operationCancellationToken));
            },
            cancellationToken);
    }

    public async Task<AdminRoleResult> UpdateRoleAsync(
        Guid roleId,
        SaveRoleRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var input = Validate(request, out var errors);
        if (input is null)
        {
            return AdminRoleResult.ValidationFailed(errors);
        }

        return await operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.RoleManagement,
            async operationCancellationToken =>
            {
                var role = await roleRepository.GetForUpdateAsync(roleId, operationCancellationToken);
                if (role is null)
                {
                    return AdminRoleResult.Missing();
                }

                if (role.IsSystem)
                {
                    return AdminRoleResult.SystemLocked();
                }

                if (await roleRepository.NormalizedNameExistsAsync(input.NormalizedName, roleId, operationCancellationToken))
                {
                    return AdminRoleResult.NameTaken();
                }

                var removesRoleManagement = GrantsRoleManagement(RolePermissionRules.Resolve(role))
                    && !input.Permissions.Contains(Permissions.ManageRoles);
                if (removesRoleManagement)
                {
                    var actor = await roleRepository.GetUserRoleAsync(actorUserId, operationCancellationToken);
                    if (actor?.RoleId == roleId)
                    {
                        return AdminRoleResult.OwnManagerRemoval();
                    }

                    var remainingManagers = await roleRepository.CountActiveRoleManagersAsync(
                        excludeUserId: null,
                        excludeRoleId: roleId,
                        operationCancellationToken);
                    if (remainingManagers == 0)
                    {
                        return AdminRoleResult.LastManager();
                    }
                }

                role.Name = input.Name;
                role.NormalizedName = input.NormalizedName;
                role.Description = input.Description;
                SyncPermissions(role, input.Permissions);

                if (!await roleRepository.TrySaveAsync(role, operationCancellationToken))
                {
                    return AdminRoleResult.NameTaken();
                }

                return AdminRoleResult.Succeeded(await GetRequiredAsync(roleId, operationCancellationToken));
            },
            cancellationToken);
    }

    public Task<AdminRoleResult> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.RoleManagement,
            async operationCancellationToken =>
            {
                var role = await roleRepository.GetForUpdateAsync(roleId, operationCancellationToken);
                if (role is null)
                {
                    return AdminRoleResult.Missing();
                }

                if (role.IsSystem)
                {
                    return AdminRoleResult.SystemLocked();
                }

                // Role không có user thì xoá không ảnh hưởng tới ai (kể cả luật "còn người quản lý role").
                if (await roleRepository.CountUsersAsync(roleId, operationCancellationToken) > 0)
                {
                    return AdminRoleResult.InUse();
                }

                await roleRepository.DeleteAsync(role, operationCancellationToken);
                return AdminRoleResult.Succeeded();
            },
            cancellationToken);

    public async Task<AssignUserRoleResult> AssignUserRoleAsync(
        Guid userId,
        AssignUserRoleRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RoleId == Guid.Empty)
        {
            return AssignUserRoleResult.InvalidRole();
        }

        if (userId == actorUserId)
        {
            return AssignUserRoleResult.OwnRoleChange();
        }

        return await operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.RoleManagement,
            async operationCancellationToken =>
            {
                var user = await roleRepository.GetUserRoleAsync(userId, operationCancellationToken);
                if (user is null)
                {
                    return AssignUserRoleResult.UserMissing();
                }

                var targetRole = await roleRepository.GetByIdAsync(request.RoleId, operationCancellationToken);
                if (targetRole is null)
                {
                    return AssignUserRoleResult.InvalidRole();
                }

                if (user.RoleId != targetRole.Id)
                {
                    var managesRolesNow = user.Status == UserStatus.Active
                        && GrantsRoleManagement(RolePermissionRules.Resolve(
                            user.RoleNormalizedName,
                            user.RoleIsSystem,
                            user.RoleStoredPermissions));
                    var willManageRoles = GrantsRoleManagement(Map(targetRole).Permissions);
                    if (managesRolesNow && !willManageRoles)
                    {
                        var remainingManagers = await roleRepository.CountActiveRoleManagersAsync(
                            excludeUserId: userId,
                            excludeRoleId: null,
                            operationCancellationToken);
                        if (remainingManagers == 0)
                        {
                            return AssignUserRoleResult.LastManager();
                        }
                    }

                    await roleRepository.SetUserRoleAsync(userId, targetRole.Id, operationCancellationToken);
                }

                return AssignUserRoleResult.Succeeded(
                    new UserRoleAssignmentResponse(userId, targetRole.Id, targetRole.Name));
            },
            cancellationToken);
    }

    private async Task<AdminRoleResponse> GetRequiredAsync(Guid roleId, CancellationToken cancellationToken) =>
        Map(await roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new InvalidOperationException($"Role {roleId} vừa lưu nhưng không đọc lại được."));

    private static AdminRoleResponse Map(RoleReadModel role) =>
        new(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem,
            RolePermissionRules.Resolve(role.NormalizedName, role.IsSystem, role.StoredPermissions),
            role.UserCount);

    private static bool GrantsRoleManagement(IEnumerable<string> permissions) =>
        permissions.Contains(Permissions.ManageRoles);

    // Chỉ xoá quyền bị bỏ và thêm quyền mới: EF không cho xoá rồi thêm lại cùng khoá (RoleId, Permission).
    private static void SyncPermissions(Role role, IReadOnlyCollection<string> desiredPermissions)
    {
        foreach (var removed in role.Permissions.Where(permission => !desiredPermissions.Contains(permission.Permission)).ToList())
        {
            role.Permissions.Remove(removed);
        }

        foreach (var added in desiredPermissions.Where(code => role.Permissions.All(permission => permission.Permission != code)).ToList())
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = added });
        }
    }

    private static RoleInput? Validate(SaveRoleRequest request, out Dictionary<string, string[]> errors)
    {
        errors = [];

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors["name"] = ["Tên role không được để trống."];
        }
        else if (name.Length > MaxNameLength)
        {
            errors["name"] = [$"Tên role tối đa {MaxNameLength} ký tự."];
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            errors["description"] = [$"Mô tả tối đa {MaxDescriptionLength} ký tự."];
        }

        var permissions = (request.Permissions ?? [])
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknown = permissions.Where(permission => !Permissions.IsDefined(permission)).ToList();
        if (unknown.Count > 0)
        {
            errors["permissions"] = [$"Quyền không hợp lệ: {string.Join(", ", unknown)}."];
        }

        return errors.Count == 0
            ? new RoleInput(name, SystemRoles.Normalize(name), description, permissions)
            : null;
    }

    private sealed record RoleInput(string Name, string NormalizedName, string? Description, IReadOnlyList<string> Permissions);
}
