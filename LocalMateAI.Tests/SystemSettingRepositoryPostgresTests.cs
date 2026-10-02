using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class SystemSettingRepositoryPostgresTests
{
    private const string Key = SystemSettingKeys.MinActivePlacesPerStation;

    [Fact]
    public async Task Upsert_InsertsThenOverwrites_AndReturnsUpdater()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var role = await TestRoles.GetUserRoleIdAsync(c);
        var first = new User { FullName = "Admin Một", Email = "first@settings.test", RoleId = role };
        var second = new User { FullName = "Admin Hai", Email = "second@settings.test", RoleId = role };
        c.Users.AddRange(first, second);
        await c.SaveChangesAsync();
        var repository = new SystemSettingRepository(c);
        var firstAt = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);
        var secondAt = firstAt.AddHours(1);

        await repository.UpsertAsync(Key, "8", firstAt, first.Id);
        await repository.UpsertAsync(Key, "9", secondAt, second.Id);

        var row = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(Key, row.Key);
        Assert.Equal("9", row.Value);
        Assert.Equal(secondAt, row.UpdatedAt);
        Assert.Equal(second.Id, row.UpdatedByUserId);
        Assert.Equal("Admin Hai", row.UpdatedByFullName);
        Assert.Equal("second@settings.test", row.UpdatedByEmail);
        Assert.False(c.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Delete_RemovesRow_AndIsIdempotent()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var role = await TestRoles.GetUserRoleIdAsync(c);
        var admin = new User { FullName = "Admin", Email = "delete@settings.test", RoleId = role };
        c.Users.Add(admin);
        await c.SaveChangesAsync();
        var repository = new SystemSettingRepository(c);
        await repository.UpsertAsync(Key, "8", DateTime.UtcNow, admin.Id);

        await repository.DeleteAsync(Key);
        await repository.DeleteAsync(Key);

        Assert.Empty(await repository.GetAllAsync());
        Assert.Equal(0, await c.SystemSettings.CountAsync());
    }
}
