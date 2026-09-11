using System.Security.Claims;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LocalMateAI.Infrastructure.Security;

public sealed class JwtAccessTokenService(IOptions<JwtOptions> options) : IAccessTokenService
{
    private readonly JsonWebTokenHandler tokenHandler = new();
    private readonly JwtOptions jwtOptions = options.Value;

    public AccessTokenResult CreateAccessToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(jwtOptions.AccessTokenMinutes);
        var signingKey = new SymmetricSecurityKey(
            Convert.FromBase64String(jwtOptions.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwtOptions.Issuer,
            Audience = jwtOptions.Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("role", user.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256),
            TokenType = "JWT"
        };

        return new AccessTokenResult(tokenHandler.CreateToken(descriptor), expiresAt);
    }
}
