using System.Text.Json;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Infrastructure.Persistence;

public static class DataSeeder
{
    public static readonly IReadOnlyList<DevelopmentUserSeedDefinition> DevelopmentUsers =
    [
        new("admin@localmate.dev", "LocalMate Admin", UserRole.Admin),
        new("user1@localmate.dev", "LocalMate User 1", UserRole.User),
        new("user2@localmate.dev", "LocalMate User 2", UserRole.User),
        new("user3@localmate.dev", "LocalMate User 3", UserRole.User)
    ];

    public const string DevelopmentDefaultPassword = "LocalMate@123";

    private static readonly GeometryFactory GeometryFactory =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, string[]> PlaceTagMappingsByCategory =
        new Dictionary<string, string[]>
        {
            ["Food"] = new[] { "Ẩm thực", "Chill nhẹ" },
            ["Cafe"] = new[] { "Cà phê", "Chụp ảnh" },
            ["Culture"] = new[] { "Văn hóa", "Chụp ảnh" },
            ["CheckIn"] = new[] { "Check-in", "Chill nhẹ" }
        };

    public static async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await SeedMetroStationsAsync(context, cancellationToken);
        await SeedPlacesAsync(context, cancellationToken);
        await SeedTagsAsync(context, cancellationToken);
        await SeedPlaceTagsAsync(context, cancellationToken);
        await SeedCuratedItinerariesAsync(context, cancellationToken);
    }

    public static async Task SeedDevelopmentUsersAsync(
        AppDbContext context,
        IPasswordHashService passwordHashService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(passwordHashService);

        var existingEmails = await context.Users
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);

        var existingEmailSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);

        var usersToAdd = new List<User>();

        foreach (var seed in DevelopmentUsers)
        {
            if (existingEmailSet.Contains(seed.Email))
            {
                continue;
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = seed.FullName,
                Email = seed.Email.Trim().ToLowerInvariant(),
                Role = seed.Role,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            user.PasswordHash = passwordHashService.HashPassword(user, DevelopmentDefaultPassword);
            usersToAdd.Add(user);
        }

        if (usersToAdd.Count > 0)
        {
            await context.Users.AddRangeAsync(usersToAdd, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedMetroStationsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.MetroStations.AnyAsync(cancellationToken))
        {
            return;
        }

        var records = await ReadSeedFileAsync<MetroStationSeedRecord>("metro-stations.seed.json", cancellationToken);

        var stations = records.Select(record => new MetroStation
        {
            Name = record.Name,
            Order = record.Order,
            Location = GeometryFactory.CreatePoint(new Coordinate(record.Longitude, record.Latitude))
        });

        await context.MetroStations.AddRangeAsync(stations, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedPlacesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Places.AnyAsync(cancellationToken))
        {
            return;
        }

        var records = await ReadSeedFileAsync<PlaceSeedRecord>("places.seed.json", cancellationToken);

        var places = records.Select(record => new Place
        {
            Name = record.Name,
            Description = record.Description,
            Address = record.Address,
            Location = GeometryFactory.CreatePoint(new Coordinate(record.Longitude, record.Latitude)),
            Category = Enum.Parse<PlaceCategory>(record.Category),
            Status = PlaceStatus.Active,
            EstimatedCostMin = record.EstimatedCostMin,
            EstimatedCostMax = record.EstimatedCostMax
        });

        await context.Places.AddRangeAsync(places, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTagsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Tags.AnyAsync(cancellationToken))
        {
            return;
        }

        var records = await ReadSeedFileAsync<TagSeedRecord>("tags.seed.json", cancellationToken);

        var tags = records.Select(record => new Tag
        {
            Name = record.Name,
            Type = Enum.Parse<TagType>(record.Type),
            IsActive = true
        });

        await context.Tags.AddRangeAsync(tags, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedPlaceTagsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        // Không guard theo Places (Places có thể đã tồn tại từ trước) — guard theo chính bảng PlaceTags.
        if (await context.PlaceTags.AnyAsync(cancellationToken))
        {
            return;
        }

        var placeRecords = await ReadSeedFileAsync<PlaceSeedRecord>("places.seed.json", cancellationToken);
        var placesByName = await context.Places.ToDictionaryAsync(place => place.Name, cancellationToken);
        var tagsByName = await context.Tags.ToDictionaryAsync(tag => tag.Name, cancellationToken);

        var placeTags = new List<PlaceTag>();

        foreach (var record in placeRecords)
        {
            if (!placesByName.TryGetValue(record.Name, out var place))
            {
                continue;
            }

            var tagNames = PlaceTagMappingsByCategory.GetValueOrDefault(record.Category, []);

            foreach (var tagName in tagNames)
            {
                if (tagsByName.TryGetValue(tagName, out var tag))
                {
                    placeTags.Add(new PlaceTag { PlaceId = place.Id, TagId = tag.Id });
                }
            }
        }

        if (placeTags.Count > 0)
        {
            await context.PlaceTags.AddRangeAsync(placeTags, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedCuratedItinerariesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.CuratedItineraries.AnyAsync(cancellationToken))
        {
            return;
        }

        var records = await ReadSeedFileAsync<CuratedItinerarySeedRecord>(
            "curated-itineraries.seed.json",
            cancellationToken);
        var placesByName = await context.Places.ToDictionaryAsync(place => place.Name, cancellationToken);

        foreach (var record in records)
        {
            var itinerary = new CuratedItinerary
            {
                Title = record.Title,
                Description = record.Description,
                CoverImageUrl = record.CoverImageUrl,
                EstimatedDurationMinutes = record.EstimatedDurationMinutes,
                EstimatedCostMin = record.EstimatedCostMin,
                EstimatedCostMax = record.EstimatedCostMax
            };

            itinerary.Items = record.PlaceNames
                .Select((placeName, index) => new CuratedItineraryItem
                {
                    CuratedItinerary = itinerary,
                    PlaceId = placesByName[placeName].Id,
                    OrderIndex = index
                })
                .ToList();

            await context.CuratedItineraries.AddAsync(itinerary, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<List<T>> ReadSeedFileAsync<T>(string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Persistence", "SeedData", fileName);

        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }

    private sealed record MetroStationSeedRecord(string Name, int Order, double Latitude, double Longitude);

    private sealed record TagSeedRecord(string Name, string Type);

    private sealed record PlaceSeedRecord(
        string Name,
        string Category,
        string Address,
        double Latitude,
        double Longitude,
        decimal EstimatedCostMin,
        decimal EstimatedCostMax,
        string Description);

    private sealed record CuratedItinerarySeedRecord(
        string Title,
        string? Description,
        string? CoverImageUrl,
        int EstimatedDurationMinutes,
        decimal EstimatedCostMin,
        decimal EstimatedCostMax,
        IReadOnlyList<string> PlaceNames);

    public sealed record DevelopmentUserSeedDefinition(string Email, string FullName, UserRole Role);
}