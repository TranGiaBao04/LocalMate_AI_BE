namespace LocalMateAI.Application.DTOs.Auth;

public sealed record DemoSessionResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string SessionType);
