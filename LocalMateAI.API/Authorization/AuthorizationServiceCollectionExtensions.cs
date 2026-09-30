using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace LocalMateAI.API.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>Đăng ký policy RegisteredUser + một policy cho mỗi quyền. Program.cs và test HTTP cùng dùng.</summary>
    public static IServiceCollection AddLocalMateAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AppPolicies.RegisteredUser, policy => policy.RequireAuthenticatedUser());

            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(
                    AppPolicies.ForPermission(permission.Code),
                    policy => policy
                        .RequireAuthenticatedUser()
                        .AddRequirements(new PermissionRequirement(permission.Code)));
            }
        });

        return services;
    }
}
