using System.Security.Claims;
using System.Text.Json;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace LocalMateAI.Tests;

public sealed class AccountAccessMiddlewareTests
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task PublicEndpoint_WithToken_DoesNotQueryAccess()
    {
        var access = new FakeUserAccessService(Snapshot(UserStatus.Locked));
        var httpContext = CreateContext(new AllowAnonymousAttribute(), UserClaims());

        var nextCalled = await InvokeAsync(httpContext, access);

        Assert.True(nextCalled);
        Assert.Equal(0, access.Calls);
    }

    [Fact]
    public async Task ProtectedEndpoint_Anonymous_PassesThroughWithoutQuery()
    {
        var access = new FakeUserAccessService(Snapshot(UserStatus.Active));
        var httpContext = CreateContext(new AuthorizeAttribute(), claims: null);

        var nextCalled = await InvokeAsync(httpContext, access);

        Assert.True(nextCalled);
        Assert.Equal(0, access.Calls);
    }

    [Fact]
    public async Task ProtectedEndpoint_DemoToken_UsesDemoSnapshotWithoutQuery()
    {
        var access = new FakeUserAccessService(null);
        var httpContext = CreateContext(
            new AuthorizeAttribute(),
            [
                new Claim(AuthClaimNames.Subject, Guid.NewGuid().ToString()),
                new Claim(AuthClaimNames.SessionType, AuthClaimNames.DemoSession)
            ]);

        var nextCalled = await InvokeAsync(httpContext, access);

        Assert.True(nextCalled);
        Assert.Equal(0, access.Calls);
        Assert.Same(UserAccessSnapshot.Demo, httpContext.GetUserAccess());
    }

    [Fact]
    public async Task ProtectedEndpoint_UserMissing_Returns401AccountNotFound()
    {
        var httpContext = CreateContext(new AuthorizeAttribute(), UserClaims());

        var nextCalled = await InvokeAsync(httpContext, new FakeUserAccessService(null));

        Assert.False(nextCalled);
        await AssertProblemAsync(httpContext, StatusCodes.Status401Unauthorized, "account_not_found");
    }

    [Fact]
    public async Task ProtectedEndpoint_LockedUser_Returns403AccountLocked()
    {
        var httpContext = CreateContext(new AuthorizeAttribute(), UserClaims());

        var nextCalled = await InvokeAsync(httpContext, new FakeUserAccessService(Snapshot(UserStatus.Locked)));

        Assert.False(nextCalled);
        await AssertProblemAsync(httpContext, StatusCodes.Status403Forbidden, "account_locked");
    }

    [Fact]
    public async Task ProtectedEndpoint_ActiveUser_StoresSnapshotAndContinues()
    {
        var snapshot = Snapshot(UserStatus.Active);
        var access = new FakeUserAccessService(snapshot);
        var httpContext = CreateContext(new HasPermissionAttribute(Permissions.ManagePlaces), UserClaims());

        var nextCalled = await InvokeAsync(httpContext, access);

        Assert.True(nextCalled);
        Assert.Equal(1, access.Calls);
        Assert.Equal(UserId, access.LastUserId);
        Assert.Same(snapshot, httpContext.GetUserAccess());
    }

    [Fact]
    public async Task ProtectedEndpoint_InvalidSubject_PassesThroughWithoutQuery()
    {
        var access = new FakeUserAccessService(Snapshot(UserStatus.Active));
        var httpContext = CreateContext(new AuthorizeAttribute(), [new Claim(AuthClaimNames.Subject, "not-a-guid")]);

        var nextCalled = await InvokeAsync(httpContext, access);

        Assert.True(nextCalled);
        Assert.Equal(0, access.Calls);
        Assert.Null(httpContext.GetUserAccess());
    }

    private static UserAccessSnapshot Snapshot(UserStatus status) =>
        new(status, "User", new HashSet<string>());

    private static Claim[] UserClaims() => [new Claim(AuthClaimNames.Subject, UserId.ToString())];

    private static DefaultHttpContext CreateContext(object endpointMetadata, Claim[]? claims)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(endpointMetadata),
            "test-endpoint"));
        httpContext.User = claims is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return httpContext;
    }

    private static async Task<bool> InvokeAsync(HttpContext httpContext, IUserAccessService access)
    {
        var nextCalled = false;
        var middleware = new AccountAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext, access);
        return nextCalled;
    }

    private static async Task AssertProblemAsync(HttpContext httpContext, int status, string code)
    {
        Assert.Equal(status, httpContext.Response.StatusCode);
        httpContext.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    private sealed class FakeUserAccessService(UserAccessSnapshot? snapshot) : IUserAccessService
    {
        public int Calls { get; private set; }

        public Guid? LastUserId { get; private set; }

        public Task<UserAccessSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUserId = userId;
            return Task.FromResult(snapshot);
        }
    }
}
