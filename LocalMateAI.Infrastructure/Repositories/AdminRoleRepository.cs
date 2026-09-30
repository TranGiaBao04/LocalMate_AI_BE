using LocalMateAI.Application.DTOs.Roles;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminRoleRepository(AppDbContext dbContext) : IAdminRoleRepository
{
    private const string NormalizedNameConstraintName = "UX_Roles_NormalizedName";
    private static readonly string AdminNormalizedName = SystemRoles.Normalize(SystemRoles.AdminName);

    public async Task<IReadOnlyList<RoleReadModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Project(dbContext.Roles
                .AsNoTracking()
                .OrderByDescending(role => role.IsSystem)
                .ThenBy(role => role.Name))
            .ToListAsync(cancellationToken);

    public Task<RoleReadModel?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        Project(dbContext.Roles.AsNoTracking().Where(role => role.Id == roleId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Role?> GetForUpdateAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        dbContext.Roles
            .Include(role => role.Permissions)
            .SingleOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    public Task<bool> NormalizedNameExistsAsync(
        string normalizedName,
        Guid? excludeRoleId,
        CancellationToken cancellationToken = default) =>
        dbContext.Roles.AnyAsync(
            role => role.NormalizedName == normalizedName && (excludeRoleId == null || role.Id != excludeRoleId),
            cancellationToken);

    public async Task<bool> TryAddAsync(Role role, CancellationToken cancellationToken = default)
    {
        dbContext.Roles.Add(role);
        if (await TrySaveChangesAsync(cancellationToken))
        {
            return true;
        }

        dbContext.Entry(role).State = EntityState.Detached;
        return false;
    }

    public Task<bool> TrySaveAsync(Role role, CancellationToken cancellationToken = default)
    {
        // Chỉ đổi quyền thì bản thân Role không "modified" ⇒ tự đánh dấu để UpdatedAt được cập nhật.
        dbContext.Entry(role).Property(entry => entry.UpdatedAt).IsModified = true;
        return TrySaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Role role, CancellationToken cancellationToken = default)
    {
        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountUsersAsync(Guid roleId, CancellationToken cancellationToken = default) =>
        dbContext.Users.CountAsync(user => user.RoleId == roleId, cancellationToken);

    // Điều kiện "có ManageRoles" phải khớp RolePermissionRules: role hệ thống ADMIN, hoặc role có lưu quyền ManageRoles.
    public Task<int> CountActiveRoleManagersAsync(
        Guid? excludeUserId,
        Guid? excludeRoleId,
        CancellationToken cancellationToken = default) =>
        dbContext.Users.CountAsync(
            user => user.Status == UserStatus.Active
                && (excludeUserId == null || user.Id != excludeUserId)
                && (excludeRoleId == null || user.RoleId != excludeRoleId)
                && ((user.Role!.IsSystem && user.Role.NormalizedName == AdminNormalizedName)
                    || user.Role.Permissions.Any(permission => permission.Permission == Permissions.ManageRoles)),
            cancellationToken);

    public Task<UserRoleReadModel?> GetUserRoleAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserRoleReadModel(
                user.Id,
                user.RoleId,
                user.Status,
                user.Role!.NormalizedName,
                user.Role.IsSystem,
                user.Role.Permissions.Select(permission => permission.Permission).ToList()))
            .SingleOrDefaultAsync(cancellationToken);

    public Task SetUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.RoleId, roleId)
                    .SetProperty(user => user.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

    private IQueryable<RoleReadModel> Project(IQueryable<Role> roles) =>
        roles.Select(role => new RoleReadModel(
            role.Id,
            role.Name,
            role.NormalizedName,
            role.Description,
            role.IsSystem,
            role.Permissions.Select(permission => permission.Permission).ToList(),
            dbContext.Users.Count(user => user.RoleId == role.Id)));

    // SaveChanges trong transaction của executor dùng savepoint, nên lỗi trùng tên không làm hỏng transaction.
    private async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: NormalizedNameConstraintName
            })
        {
            return false;
        }
    }
}
