using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Tags;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PlaceRepository(AppDbContext dbContext) : IPlaceRepository
{
    public async Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(
        double radiusMeters,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.SqlQuery<MetroClusterPlaceReadModel>(
            $"""
             SELECT nearest."StationId", nearest."StationName", nearest."StationOrder",
                    nearest."StationLatitude", nearest."StationLongitude",
                    p."Id" AS "PlaceId", p."Name" AS "PlaceName", p."Address" AS "PlaceAddress",
                    ST_Y(p."Location") AS "PlaceLatitude",
                    ST_X(p."Location") AS "PlaceLongitude",
                    p."Category" AS "PlaceCategory",
                    p."EstimatedCostMin", p."EstimatedCostMax", p."ImageUrl",
                    nearest."DistanceFromStationMeters"
             FROM "Places" p
             CROSS JOIN LATERAL (
                 SELECT ms."Id" AS "StationId", ms."Name" AS "StationName",
                        ms."Order" AS "StationOrder",
                        ST_Y(ms."Location") AS "StationLatitude",
                        ST_X(ms."Location") AS "StationLongitude",
                        ST_Distance(
                            p."Location"::geography,
                            ms."Location"::geography
                        ) AS "DistanceFromStationMeters"
                 FROM "MetroStations" ms
                 ORDER BY "DistanceFromStationMeters", ms."Order", ms."Name", ms."Id"
                 LIMIT 1
             ) nearest
             WHERE p."Status" = 'Active'
               AND nearest."DistanceFromStationMeters" <= {radiusMeters}
             """)
            .ToListAsync(cancellationToken);
    }

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

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(
        IReadOnlyList<Guid> placeIds,
        CancellationToken cancellationToken = default)
    {
        if (placeIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        return (await dbContext.PlaceTags
                .Where(placeTag => placeIds.Contains(placeTag.PlaceId))
                .Select(placeTag => new { placeTag.PlaceId, placeTag.TagId })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.PlaceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group.Select(row => row.TagId).ToList());
    }

    public async Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Places
            .AsNoTracking()
            .OrderBy(place => place.Name)
            .ThenBy(place => place.Id)
            .Select(place => new AdminPlaceResponse(
                place.Id,
                place.Name,
                place.Description,
                place.Address,
                place.Location.Y,
                place.Location.X,
                place.Category.ToString(),
                place.Status.ToString(),
                place.IsVerified,
                place.EstimatedCostMin,
                place.EstimatedCostMax,
                place.ImageUrl,
                place.CreatedAt,
                place.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminPlaceResponse?> GetAdminByIdAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Places
            .AsNoTracking()
            .Where(place => place.Id == placeId)
            .Select(place => new AdminPlaceResponse(
                place.Id,
                place.Name,
                place.Description,
                place.Address,
                place.Location.Y,
                place.Location.X,
                place.Category.ToString(),
                place.Status.ToString(),
                place.IsVerified,
                place.EstimatedCostMin,
                place.EstimatedCostMax,
                place.ImageUrl,
                place.CreatedAt,
                place.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Place?> GetByIdAsync(
        Guid placeId,
        CancellationToken cancellationToken = default) =>
        dbContext.Places.SingleOrDefaultAsync(place => place.Id == placeId, cancellationToken);

    public async Task AddAsync(
        Place place,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Places.AddAsync(place, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DeletePlacePersistenceResult> DeleteForAdminAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var place = await dbContext.Places.SingleOrDefaultAsync(
            candidate => candidate.Id == placeId,
            cancellationToken);

        if (place is null)
        {
            return DeletePlacePersistenceResult.NotFound;
        }

        var isReferenced = await dbContext.ItineraryItems.AnyAsync(
                item => item.PlaceId == placeId,
                cancellationToken)
            || await dbContext.CuratedItineraryItems.AnyAsync(
                item => item.PlaceId == placeId,
                cancellationToken)
            || await dbContext.PlaceReviews.AnyAsync(
                review => review.PlaceId == placeId,
                cancellationToken);

        if (isReferenced)
        {
            return DeletePlacePersistenceResult.InUse;
        }

        var placeTags = await dbContext.PlaceTags
            .Where(placeTag => placeTag.PlaceId == placeId)
            .ToListAsync(cancellationToken);

        dbContext.PlaceTags.RemoveRange(placeTags);
        dbContext.Places.Remove(place);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return DeletePlacePersistenceResult.Deleted;
        }
        catch (DbUpdateException exception) when (IsHistoricalPlaceReferenceViolation(exception))
        {
            return DeletePlacePersistenceResult.InUse;
        }
    }

    private static bool IsHistoricalPlaceReferenceViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: "FK_ItineraryItems_Places_PlaceId"
                or "FK_CuratedItineraryItems_Places_PlaceId"
                or "FK_PlaceReviews_Places_PlaceId"
        };

    public async Task<PlaceReadModel?> GetActiveByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var place = await dbContext.Places
            .AsNoTracking()
            .Where(candidate => candidate.Id == id && candidate.Status == PlaceStatus.Active)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.Description,
                candidate.Address,
                Latitude = candidate.Location.Y,
                Longitude = candidate.Location.X,
                candidate.Category,
                candidate.Status,
                candidate.IsVerified,
                candidate.EstimatedCostMin,
                candidate.EstimatedCostMax,
                candidate.ImageUrl
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (place is null)
        {
            return null;
        }

        var tags = await dbContext.PlaceTags
            .AsNoTracking()
            .Where(placeTag => placeTag.PlaceId == id && placeTag.Tag.IsActive)
            .OrderBy(placeTag => placeTag.Tag.Name)
            .Select(placeTag => new TagResponse(
                placeTag.Tag.Id,
                placeTag.Tag.Name,
                placeTag.Tag.Type.ToString()))
            .ToListAsync(cancellationToken);

        return new PlaceReadModel(
            place.Id,
            place.Name,
            place.Description,
            place.Address,
            place.Latitude,
            place.Longitude,
            place.Category.ToString(),
            place.Status.ToString(),
            place.IsVerified,
            place.EstimatedCostMin,
            place.EstimatedCostMax,
            place.ImageUrl,
            tags);
    }
}
