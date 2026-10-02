namespace LocalMateAI.Application.DTOs.Tags;

public sealed record TagResponse(
    Guid Id,
    string Name,
    string Type);
