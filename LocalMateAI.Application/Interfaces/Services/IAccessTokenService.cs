using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAccessTokenService
{
    // roleName truyền riêng vì user vừa tạo chưa nạp navigation Role.
    AccessTokenResult CreateAccessToken(User user, string roleName);

    AccessTokenResult CreateDemoAccessToken(Guid sessionId);
}
