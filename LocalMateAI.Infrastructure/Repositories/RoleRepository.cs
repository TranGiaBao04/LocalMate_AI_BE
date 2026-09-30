using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class RoleRepository(AppDbContext dbContext) : IRoleRepository
{
    public Task<SystemRoleReference?> GetSystemRoleAsync(
        string normalizedName,
        CancellationToken cancellationToken = default) =>
        dbContext.Roles
            .AsNoTracking()
            .Where(role => role.IsSystem && role.NormalizedName == normalizedName)
            .Select(role => new SystemRoleReference(role.Id, role.Name))
            .SingleOrDefaultAsync(cancellationToken);
}
