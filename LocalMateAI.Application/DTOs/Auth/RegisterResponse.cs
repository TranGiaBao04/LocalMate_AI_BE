namespace LocalMateAI.Application.DTOs.Auth;

public sealed record RegisterResponse(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    DateTime CreatedAt);
