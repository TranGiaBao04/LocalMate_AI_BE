using System.Reflection;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Controllers;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.Tests;

/// <summary>
/// BE-82: chặn việc quay lại kiểm role bằng chuỗi. API admin dùng [HasPermission(...)],
/// API người dùng dùng [Authorize(Policy = AppPolicies.RegisteredUser)].
/// </summary>
public sealed class AuthorizationAttributeGuardTests
{
    private static readonly IReadOnlyList<MemberInfo> ControllerMembers = typeof(AuthController).Assembly
        .GetTypes()
        .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
        .SelectMany(type => new MemberInfo[] { type }
            .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)))
        .ToList();

    [Fact]
    public void Controllers_DoNotUseRoleStrings()
    {
        var withRoles = ControllerMembers
            .SelectMany(member => member.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
                .Where(attribute => !string.IsNullOrEmpty(attribute.Roles))
                .Select(_ => $"{member.DeclaringType?.Name ?? member.Name}.{member.Name}"))
            .ToList();

        Assert.Empty(withRoles);
    }

    [Fact]
    public void HasPermission_UsesOnlyDefinedPermissions()
    {
        var permissions = ControllerMembers
            .SelectMany(member => member.GetCustomAttributes<HasPermissionAttribute>(inherit: false))
            .Select(attribute => attribute.Permission)
            .ToList();

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.True(Permissions.IsDefined(permission), permission));
    }

    [Fact]
    public void AuthorizePolicies_AreRegisteredPolicies()
    {
        var knownPolicies = Permissions.All
            .Select(permission => AppPolicies.ForPermission(permission.Code))
            .Append(AppPolicies.RegisteredUser)
            .ToHashSet();

        var unknownPolicies = ControllerMembers
            .SelectMany(member => member.GetCustomAttributes<AuthorizeAttribute>(inherit: false))
            .Select(attribute => attribute.Policy)
            .Where(policy => !string.IsNullOrEmpty(policy) && !knownPolicies.Contains(policy))
            .ToList();

        Assert.Empty(unknownPolicies);
    }
}
