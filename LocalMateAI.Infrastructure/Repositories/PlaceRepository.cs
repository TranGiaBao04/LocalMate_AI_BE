using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PlaceRepository(AppDbContext dbContext) : IPlaceRepository
{
    public async Task<Point?> GetLocationAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Places
            .Where(place => place.Id == placeId)
            .Select(place => place.Location)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        PlaceCategory? category,
        CancellationToken cancellationToken = default)
    {
        var categoryFilter = category?.ToString();

        return await dbContext.Database.SqlQuery<PlaceSummaryResponse>(
            $"""
             SELECT p."Id", p."Name", p."Address",
                    ST_Y(p."Location") AS "Latitude", ST_X(p."Location") AS "Longitude",
                    p."Category", p."EstimatedCostMin", p."EstimatedCostMax", p."ImageUrl"
             FROM "Places" p
             WHERE p."Status" = 'Active'
               AND ST_DWithin(
                   p."Location"::geography,
                   ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography,
                   {radiusMeters}
               )
               AND ({categoryFilter}::text IS NULL OR p."Category" = {categoryFilter})
             ORDER BY p."Name"
             """)
            .ToListAsync(cancellationToken);
    }
}
