using System.Text.Json;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Infrastructure.Persistence;

public static class DataSeeder
{
    private static readonly GeometryFactory GeometryFactory =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await SeedMetroStationsAsync(context, cancellationToken);
        await SeedPlacesAsync(context, cancellationToken);
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

    private static async Task<List<T>> ReadSeedFileAsync<T>(string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Persistence", "SeedData", fileName);

        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }

    private sealed record MetroStationSeedRecord(string Name, int Order, double Latitude, double Longitude);

    private sealed record PlaceSeedRecord(
        string Name,
        string Category,
        string Address,
        double Latitude,
        double Longitude,
        decimal EstimatedCostMin,
        decimal EstimatedCostMax,
        string Description);
}
