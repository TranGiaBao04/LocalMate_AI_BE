using System.Security.Claims;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LocalMateAI.Infrastructure.Security;

public sealed class JwtAccessTokenService(IOptions<JwtOptions> options) : IAccessTokenService
{
    private readonly JsonWebTokenHandler tokenHandler = new();
    private readonly JwtOptions jwtOptions = options.Value;

    public AccessTokenResult CreateAccessToken(User user, string roleName) =>
        CreateToken(
            user.Id,
            [
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("role", roleName)
            ]);

    public AccessTokenResult CreateDemoAccessToken(Guid sessionId) =>
            CreateToken(
                sessionId,
                [
                    new Claim(AuthClaimNames.SessionType, AuthClaimNames.DemoSession),
                    new Claim("role", SystemRoles.UserName)
                ]);

    private AccessTokenResult CreateToken(Guid subjectId, IEnumerable<Claim> additionalClaims)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(jwtOptions.AccessTokenMinutes);
        var signingKey = new SymmetricSecurityKey(
            Convert.FromBase64String(jwtOptions.SigningKey));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(additionalClaims);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwtOptions.Issuer,
            Audience = jwtOptions.Audience,
            Subject = new ClaimsIdentity(claims),
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
