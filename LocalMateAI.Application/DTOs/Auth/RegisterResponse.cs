namespace LocalMateAI.Application.DTOs.Auth;

public sealed record RegisterResponse(
    long Id,
    string FullName,
    string Email,
    string Role,
    DateTime CreatedAt);
