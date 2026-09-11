namespace LocalMateAI.Application.DTOs.Users;

public sealed record UserProfileResponse(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    DateTime CreatedAt);
