using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class UserAccessRepository(AppDbContext dbContext) : IUserAccessRepository
{
    // 1 truy vấn theo khoá chính: trạng thái + role + quyền đã lưu. Chạy ở mỗi request cần đăng nhập (không cache).
    public Task<UserAccessReadModel?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserAccessReadModel(
                user.Status,
                user.Role!.Name,
                user.Role.NormalizedName,
                user.Role.IsSystem,
                user.Role.Permissions.Select(permission => permission.Permission).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
}
