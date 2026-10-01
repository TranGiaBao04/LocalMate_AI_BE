using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Infrastructure.Persistence;

public static class PlanFeatureBaseline
{
    public static readonly Guid MetroGoogleMapsId = new("30000000-0000-0000-0000-000000000001");

    public static PlanFeature MetroGoogleMaps() => new()
    {
        Id = MetroGoogleMapsId,
        Code = "METRO_GOOGLE_MAPS",
        Name = "Bản đồ Metro & chỉ đường Google Maps",
        IsSystem = true
    };
}
