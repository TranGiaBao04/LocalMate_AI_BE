using LocalMateAI.Application.DTOs.Settings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class SystemSettingRepository(AppDbContext context) : ISystemSettingRepository
{
    public async Task<IReadOnlyList<SystemSettingRow>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await (
                from setting in context.SystemSettings.AsNoTracking()
                join user in context.Users.AsNoTracking() on setting.UpdatedByUserId equals (Guid?)user.Id into users
                from user in users.DefaultIfEmpty()
                select new SystemSettingRow(setting.Key, setting.Value, setting.UpdatedAt, setting.UpdatedByUserId,
                    user != null ? user.FullName : null, user != null ? user.Email : null))
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(string key, string value, DateTime updatedAt, Guid updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "SystemSettings" ("Key", "Value", "UpdatedAt", "UpdatedByUserId")
            VALUES ({key}, {value}, {updatedAt}, {updatedByUserId})
            ON CONFLICT ("Key") DO UPDATE
            SET "Value" = EXCLUDED."Value",
                "UpdatedAt" = EXCLUDED."UpdatedAt",
                "UpdatedByUserId" = EXCLUDED."UpdatedByUserId"
            """, cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await context.SystemSettings
            .Where(setting => setting.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
