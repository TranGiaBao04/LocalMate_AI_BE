using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Infrastructure.Persistence;

public static class SingleItineraryBaseline
{
    public static readonly Guid VersionId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static SingleItineraryProductVersion Version() => new()
    {
        Id = VersionId, VersionNumber = 1, Price = 29000m,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PublishedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
