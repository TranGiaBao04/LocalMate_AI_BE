using Microsoft.AspNetCore.Authorization;

namespace LocalMateAI.API.Authorization;

/// <summary>BE-82: API chỉ cho người có quyền này gọi, ví dụ <c>[HasPermission(Permissions.ManagePlaces)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(AppPolicies.ForPermission(permission))
{
    public string Permission { get; } = permission;
}
