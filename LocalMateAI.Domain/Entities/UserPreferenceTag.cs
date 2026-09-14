namespace LocalMateAI.Domain.Entities;

public sealed class UserPreferenceTag
{
    public Guid UserId { get; set; }

    public Guid TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
