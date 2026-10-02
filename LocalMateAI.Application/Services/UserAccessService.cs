using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;

namespace LocalMateAI.Application.Services;

public sealed class UserAccessService(IUserAccessRepository userAccessRepository) : IUserAccessService
{
    public async Task<UserAccessSnapshot?> GetSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var access = await userAccessRepository.GetAsync(userId, cancellationToken);
        if (access is null)
        {
            return null;
        }

        var permissions = RolePermissionRules.Resolve(
            access.RoleNormalizedName,
            access.RoleIsSystem,
            access.StoredPermissions);

        return new UserAccessSnapshot(access.Status, access.RoleName, permissions.ToHashSet(StringComparer.Ordinal));
    }
}
