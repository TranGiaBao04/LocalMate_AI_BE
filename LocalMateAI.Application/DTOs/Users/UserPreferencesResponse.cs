namespace LocalMateAI.Application.DTOs.Users;

public sealed record UserPreferencesResponse(
    Guid[] InterestTagIds,
    Guid[] TravelStyleTagIds);
