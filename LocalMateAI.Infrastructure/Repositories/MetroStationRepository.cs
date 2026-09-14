using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class MetroStationRepository(AppDbContext dbContext) : IMetroStationRepository
{
    public async Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.MetroStations
            .OrderBy(station => station.Order)
            .Select(station => new MetroStationSummaryResponse(
                station.Id,
                station.Name,
                station.Order,
                station.Location.Y,
                station.Location.X))
            .ToListAsync(cancellationToken);
    }

    public async Task<NearestStationResult?> FindNearestAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var nearest = await dbContext.Database.SqlQuery<NearestStationRow>(
            $"""
             SELECT ms."Id" AS "StationId", ms."Name" AS "StationName",
                    ST_Y(ms."Location") AS "StationLatitude",
                    ST_X(ms."Location") AS "StationLongitude",
                    ST_Distance(
                        ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography,
                        ms."Location"::geography
                    ) AS "DistanceMeters"
             FROM "MetroStations" ms
             ORDER BY "DistanceMeters"
             LIMIT 1
             """)
            .FirstOrDefaultAsync(cancellationToken);

        return nearest is null
            ? null
            : new NearestStationResult(
                nearest.StationId,
                nearest.StationName,
                nearest.StationLatitude,
                nearest.StationLongitude,
                nearest.DistanceMeters);
    }

    private sealed record NearestStationRow(
        Guid StationId,
        string StationName,
        double StationLatitude,
        double StationLongitude,
        double DistanceMeters);
}
