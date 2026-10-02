using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class RolePermissionRulesTests
{
    [Fact]
    public void Resolve_SystemAdmin_GetsEveryPermissionInCatalogOrder()
    {
        var permissions = RolePermissionRules.Resolve("ADMIN", isSystemRole: true, storedPermissions: []);

        Assert.Equal(Permissions.All.Select(permission => permission.Code), permissions);
    }

    [Fact]
    public void Resolve_CustomRoleNamedAdmin_DoesNotGetEveryPermission()
    {
        var permissions = RolePermissionRules.Resolve("ADMIN", isSystemRole: false, storedPermissions: []);

        Assert.Empty(permissions);
    }

    [Fact]
    public void Resolve_SystemUserRole_HasNoPermission()
    {
        var permissions = RolePermissionRules.Resolve("USER", isSystemRole: true, storedPermissions: []);

        Assert.Empty(permissions);
    }

    [Fact]
    public void Resolve_CustomRole_KeepsKnownStoredPermissionsInCatalogOrder()
    {
        var permissions = RolePermissionRules.Resolve(
            "KẾ TOÁN",
            isSystemRole: false,
            storedPermissions: [Permissions.ManageUsers, "RemovedPermission", Permissions.ViewRevenue]);

        Assert.Equal([Permissions.ViewRevenue, Permissions.ManageUsers], permissions);
    }

    [Fact]
    public void Resolve_RoleEntity_UsesLoadedPermissions()
    {
        var role = new Role
        {
            Name = "Kế toán",
            NormalizedName = "KẾ TOÁN",
            Permissions = [new RolePermission { Permission = Permissions.ViewRevenue }]
        };

        Assert.Equal([Permissions.ViewRevenue], RolePermissionRules.Resolve(role));
    }

    [Fact]
    public void PermissionCatalog_CodesAreUniqueAndDefined()
    {
        var codes = Permissions.All.Select(permission => permission.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, code => Assert.True(Permissions.IsDefined(code)));
        Assert.False(Permissions.IsDefined("NotAPermission"));
    }
}
