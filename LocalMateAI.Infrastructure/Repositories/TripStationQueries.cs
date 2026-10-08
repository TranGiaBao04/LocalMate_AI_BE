using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

/// <summary>Ga gần nhất của một chặng trong trip, kèm khoảng cách đường chim bay tới ga đó.</summary>
internal sealed record ItemStationRow(Guid ItemId, string StationName, int StationOrder, double DistanceMeters);

/// <summary>Ga gần nhất của một toạ độ.</summary>
internal sealed record TripOriginStationRow(string StationName, int StationOrder, double DistanceMeters);

/// <summary>
/// Truy vấn ga gần nhất dùng chung cho chi tiết trip và xoá chặng. Cùng cách tính với
/// MetroStationRepository.FindNearestAsync: đường chim bay trên geography, hai ga cách đều thì lấy ga Order nhỏ hơn.
/// </summary>
internal static class TripStationQueries
{
    public static Task<List<ItemStationRow>> GetItemStationsAsync(
        AppDbContext dbContext, Guid tripId, CancellationToken cancellationToken) =>
        dbContext.Database.SqlQuery<ItemStationRow>(
                $"""
                 SELECT i."Id" AS "ItemId", s."Name" AS "StationName", s."Order" AS "StationOrder",
                        s."DistanceMeters"
                 FROM "ItineraryItems" i
                 JOIN "Places" p ON p."Id" = i."PlaceId"
                 CROSS JOIN LATERAL (
                     SELECT ms."Name", ms."Order",
                            ST_Distance(p."Location"::geography, ms."Location"::geography) AS "DistanceMeters"
                     FROM "MetroStations" ms
                     ORDER BY "DistanceMeters", ms."Order"
                     LIMIT 1
                 ) s
                 WHERE i."TripId" = {tripId}
                 """)
            .ToListAsync(cancellationToken);

    public static Task<TripOriginStationRow?> GetNearestStationAsync(
        AppDbContext dbContext, double latitude, double longitude, CancellationToken cancellationToken) =>
        dbContext.Database.SqlQuery<TripOriginStationRow>(
                $"""
                 SELECT ms."Name" AS "StationName", ms."Order" AS "StationOrder",
                        ST_Distance(
                            ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography,
                            ms."Location"::geography
                        ) AS "DistanceMeters"
                 FROM "MetroStations" ms
                 ORDER BY "DistanceMeters", ms."Order"
                 LIMIT 1
                 """)
            .FirstOrDefaultAsync(cancellationToken);
}
