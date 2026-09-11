using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAccessTokenService
{
    AccessTokenResult CreateAccessToken(User user);

    AccessTokenResult CreateDemoAccessToken(Guid sessionId);
}
