namespace LocalMateAI.Application.DTOs.Users;

public sealed record UpdateCurrentUserRequest(
    string? FullName,
    UpdateUserPreferencesRequest? Preferences);
