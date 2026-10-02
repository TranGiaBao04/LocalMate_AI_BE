using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class UserAccessServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_UserMissing_ReturnsNull()
    {
        var service = new UserAccessService(new FakeUserAccessRepository(null));

        Assert.Null(await service.GetSnapshotAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetSnapshotAsync_SystemAdmin_HasEveryPermission()
    {
        var service = new UserAccessService(new FakeUserAccessRepository(
            new UserAccessReadModel(UserStatus.Active, "Admin", "ADMIN", true, [])));

        var snapshot = await service.GetSnapshotAsync(Guid.NewGuid());

        Assert.NotNull(snapshot);
        Assert.Equal("Admin", snapshot.RoleName);
        Assert.All(Permissions.All, permission => Assert.True(snapshot.HasPermission(permission.Code)));
    }

    [Fact]
    public async Task GetSnapshotAsync_CustomRole_KeepsStoredPermissionsAndStatus()
    {
        var service = new UserAccessService(new FakeUserAccessRepository(
            new UserAccessReadModel(UserStatus.Locked, "Kế toán", "KẾ TOÁN", false, [Permissions.ViewRevenue])));

        var snapshot = await service.GetSnapshotAsync(Guid.NewGuid());

        Assert.NotNull(snapshot);
        Assert.Equal(UserStatus.Locked, snapshot.Status);
        Assert.True(snapshot.HasPermission(Permissions.ViewRevenue));
        Assert.False(snapshot.HasPermission(Permissions.ManagePlaces));
    }

    private sealed class FakeUserAccessRepository(UserAccessReadModel? access) : IUserAccessRepository
    {
        public Task<UserAccessReadModel?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(access);
    }
}
