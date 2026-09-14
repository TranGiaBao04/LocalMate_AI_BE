namespace LocalMateAI.Application.DTOs.Auth;

public sealed record AccessTokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAt);
