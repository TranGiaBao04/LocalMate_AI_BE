using LocalMateAI.Application.DTOs.Roles;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminRoleService
{
    IReadOnlyList<PermissionResponse> GetPermissions();

    Task<IReadOnlyList<AdminRoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<AdminRoleResponse?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<AdminRoleResult> CreateRoleAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);

    Task<AdminRoleResult> UpdateRoleAsync(
        Guid roleId,
        SaveRoleRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<AdminRoleResult> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<AssignUserRoleResult> AssignUserRoleAsync(
        Guid userId,
        AssignUserRoleRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}
