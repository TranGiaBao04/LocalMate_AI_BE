using LocalMateAI.Application.DTOs.Stations;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminStationRepository(AppDbContext context) : IAdminStationRepository
{
    public async Task<AdminStationSnapshot> GetStationPlaceCountsAsync(double radiusMeters,
        CancellationToken cancellationToken = default)
    {
        // Một truy vấn duy nhất: ga và số đếm cùng một snapshot.
        // Gán ga y hệt PlaceRepository.GetMetroClusterPlacesAsync: ga gần nhất (hoà ⇒ Order, Name, Id),
        // chỉ tính khi khoảng cách ≤ bán kính. Nhánh UNION ALL thứ hai là các địa điểm ngoài vùng phủ.
        var rows = await context.Database.SqlQuery<StationCountSqlRow>($"""
            WITH assigned AS (
                SELECT p."Category", p."Status",
                       CASE WHEN nearest."Distance" <= {radiusMeters} THEN nearest."Id" END AS "StationId"
                FROM "Places" p
                LEFT JOIN LATERAL (
                    SELECT ms."Id",
                           ST_Distance(p."Location"::geography, ms."Location"::geography) AS "Distance"
                    FROM "MetroStations" ms
                    ORDER BY "Distance", ms."Order", ms."Name", ms."Id"
                    LIMIT 1
                ) nearest ON true
                WHERE p."DeletedAt" IS NULL
            ),
            counts AS (
                SELECT "StationId", "Category", "Status", count(*)::int AS "PlaceCount"
                FROM assigned
                GROUP BY "StationId", "Category", "Status"
            )
            SELECT ms."Id" AS "StationId", ms."Order" AS "StationOrder", ms."Name" AS "StationName",
                   ST_Y(ms."Location") AS "Latitude", ST_X(ms."Location") AS "Longitude",
                   c."Category", c."Status", c."PlaceCount"
            FROM "MetroStations" ms
            LEFT JOIN counts c ON c."StationId" = ms."Id"
            UNION ALL
            SELECT NULL, NULL, NULL, NULL, NULL, c."Category", c."Status", c."PlaceCount"
            FROM counts c
            WHERE c."StationId" IS NULL
            """).ToListAsync(cancellationToken);

        var stations = rows
            .Where(row => row.StationId is not null)
            .DistinctBy(row => row.StationId)
            .Select(row => new AdminStationReadModel(row.StationId!.Value, row.StationOrder!.Value,
                row.StationName!, row.Latitude!.Value, row.Longitude!.Value))
            .ToList();

        // Ga không có địa điểm nào trả 1 dòng với Category = null ⇒ không phải số đếm.
        var counts = rows
            .Where(row => row.Category is not null)
            .Select(row => new StationPlaceCount(row.StationId, Enum.Parse<PlaceCategory>(row.Category!),
                Enum.Parse<PlaceStatus>(row.Status!), row.PlaceCount!.Value))
            .ToList();

        return new AdminStationSnapshot(stations, counts);
    }

    private sealed class StationCountSqlRow
    {
        public Guid? StationId { get; init; }
        public int? StationOrder { get; init; }
        public string? StationName { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public string? Category { get; init; }
        public string? Status { get; init; }
        public int? PlaceCount { get; init; }
    }
}
