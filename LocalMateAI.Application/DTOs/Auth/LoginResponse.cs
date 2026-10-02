namespace LocalMateAI.Application.DTOs.Auth;

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt);
