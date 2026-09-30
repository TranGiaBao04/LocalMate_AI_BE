using System.Security.Claims;
using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace LocalMateAI.Tests;

public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task Handle_SnapshotHasPermission_Succeeds()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetUserAccess(Snapshot(Permissions.ManagePlaces));

        Assert.True(await AuthorizeAsync(httpContext, Permissions.ManagePlaces));
    }

    [Fact]
    public async Task Handle_SnapshotMissingPermission_Fails()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetUserAccess(Snapshot(Permissions.ViewRevenue));

        Assert.False(await AuthorizeAsync(httpContext, Permissions.ManagePlaces));
    }

    [Fact]
    public async Task Handle_NoSnapshot_Fails()
    {
        Assert.False(await AuthorizeAsync(new DefaultHttpContext(), Permissions.ManagePlaces));
    }

    [Fact]
    public async Task Handle_DemoSnapshot_HasNoAdminPermission()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetUserAccess(UserAccessSnapshot.Demo);

        Assert.False(await AuthorizeAsync(httpContext, Permissions.ManagePlaces));
    }

    private static UserAccessSnapshot Snapshot(params string[] permissions) =>
        new(UserStatus.Active, "Test", permissions.ToHashSet());

    private static async Task<bool> AuthorizeAsync(HttpContext httpContext, string permission)
    {
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity([], "Test")),
            httpContext);

        await new PermissionAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }
}
