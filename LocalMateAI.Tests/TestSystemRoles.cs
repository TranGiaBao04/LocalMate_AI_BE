using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Common;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

internal sealed class FakeSystemRoleProvider : ISystemRoleProvider
{
    public static readonly SystemRoleReference User =
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), SystemRoles.UserName);

    public static readonly SystemRoleReference Admin =
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), SystemRoles.AdminName);

    public Task<SystemRoleReference> GetUserRoleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(User);

    public Task<SystemRoleReference> GetAdminRoleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Admin);
}

internal static class TestRoles
{
    // Id role User trong DB thật (test Postgres), do migration sinh nên phải tra theo tên.
    public static Task<Guid> GetUserRoleIdAsync(AppDbContext context)
    {
        var normalizedName = SystemRoles.Normalize(SystemRoles.UserName);
        return context.Roles
            .AsNoTracking()
            .Where(role => role.IsSystem && role.NormalizedName == normalizedName)
            .Select(role => role.Id)
            .SingleAsync();
    }
}
