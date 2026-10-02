namespace LocalMateAI.Application.DTOs.Users;

public sealed record UpdateUserPreferencesRequest(
    Guid[]? InterestTagIds,
    Guid[]? TravelStyleTagIds);
