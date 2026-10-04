using LocalMateAI.Application.DTOs.Search;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

// So khớp không dấu, không phân biệt hoa thường: unaccent(cột) ILIKE unaccent(mẫu).
// Ký tự thoát của ILIKE mặc định là "\" nên mẫu đã escape ở LikePatterns dùng được ngay.
public sealed class SearchRepository(AppDbContext dbContext) : ISearchRepository
{
    public async Task<IReadOnlyList<SearchStationItem>> SearchStationsAsync(
        string keyword,
        int take,
        double clusterRadiusMeters,
        CancellationToken cancellationToken = default)
    {
        var (prefix, contains) = LikePatterns(keyword);

        // PlaceCount gán địa điểm vào ga y hệt PlaceRepository.GetMetroClusterPlacesAsync
        // (ga gần nhất, chỉ tính khi trong bán kính cụm ga) nên khớp số của metro-clusters.
        return await dbContext.Database.SqlQuery<SearchStationItem>(
            $"""
             WITH matched AS (
                 SELECT ms."Id", ms."Order", ms."Name",
                        CASE WHEN unaccent(ms."Name") ILIKE unaccent({prefix}) THEN 0 ELSE 1 END AS "MatchRank"
                 FROM "MetroStations" ms
                 WHERE unaccent(ms."Name") ILIKE unaccent({contains})
                 ORDER BY "MatchRank", ms."Order", ms."Id"
                 LIMIT {take}
             ),
             counts AS (
                 SELECT nearest."Id" AS "StationId", count(*)::int AS "PlaceCount"
                 FROM "Places" p
                 CROSS JOIN LATERAL (
                     SELECT s."Id",
                            ST_Distance(p."Location"::geography, s."Location"::geography) AS "Distance"
                     FROM "MetroStations" s
                     ORDER BY "Distance", s."Order", s."Name", s."Id"
                     LIMIT 1
                 ) nearest
                 WHERE p."Status" = 'Active'
                   AND p."DeletedAt" IS NULL
                   AND nearest."Distance" <= {clusterRadiusMeters}
                 GROUP BY nearest."Id"
             )
             SELECT m."Id", m."Order", m."Name", COALESCE(c."PlaceCount", 0) AS "PlaceCount"
             FROM matched m
             LEFT JOIN counts c ON c."StationId" = m."Id"
             ORDER BY m."MatchRank", m."Order", m."Id"
             """)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchPlaceItem>> SearchPlacesAsync(
        string keyword,
        int take,
        double clusterRadiusMeters,
        CancellationToken cancellationToken = default)
    {
        var (prefix, contains) = LikePatterns(keyword);

        // MatchRank: 0 tên bắt đầu bằng từ khoá, 1 tên chứa, 2 khớp tag, 3 khớp địa chỉ.
        // Lọc + cắt LIMIT trước rồi mới tìm ga gần nhất, để chỉ tính khoảng cách cho các dòng trả về.
        var rows = await dbContext.Database.SqlQuery<PlaceRow>(
            $"""
             SELECT m."Id", m."Name", m."Address", m."Category", m."ImageUrl",
                    m."EstimatedCostMin", m."EstimatedCostMax", m."MatchRank",
                    CASE WHEN nearest."Distance" <= {clusterRadiusMeters} THEN nearest."Order" END AS "StationOrder",
                    CASE WHEN nearest."Distance" <= {clusterRadiusMeters} THEN nearest."Name" END AS "StationName"
             FROM (
                 SELECT p."Id", p."Name", p."Address", p."Category", p."ImageUrl", p."Location",
                        p."EstimatedCostMin", p."EstimatedCostMax", p."IsVerified", r."MatchRank"
                 FROM "Places" p
                 CROSS JOIN LATERAL (
                     SELECT CASE
                         WHEN unaccent(p."Name") ILIKE unaccent({prefix}) THEN 0
                         WHEN unaccent(p."Name") ILIKE unaccent({contains}) THEN 1
                         WHEN EXISTS (
                             SELECT 1
                             FROM "PlaceTags" pt
                             JOIN "Tags" t ON t."Id" = pt."TagId"
                             WHERE pt."PlaceId" = p."Id"
                               AND t."IsActive"
                               AND unaccent(t."Name") ILIKE unaccent({contains})) THEN 2
                         WHEN unaccent(p."Address") ILIKE unaccent({contains}) THEN 3
                     END AS "MatchRank"
                 ) r
                 WHERE p."Status" = 'Active'
                   AND p."DeletedAt" IS NULL
                   AND r."MatchRank" IS NOT NULL
                 ORDER BY r."MatchRank", p."IsVerified" DESC, p."Name", p."Id"
                 LIMIT {take}
             ) m
             LEFT JOIN LATERAL (
                 SELECT ms."Order", ms."Name",
                        ST_Distance(m."Location"::geography, ms."Location"::geography) AS "Distance"
                 FROM "MetroStations" ms
                 ORDER BY "Distance", ms."Order", ms."Name", ms."Id"
                 LIMIT 1
             ) nearest ON true
             ORDER BY m."MatchRank", m."IsVerified" DESC, m."Name", m."Id"
             """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new SearchPlaceItem(
                row.Id,
                row.Name,
                row.Address,
                row.Category,
                row.ImageUrl,
                row.EstimatedCostMin,
                row.EstimatedCostMax,
                row.StationOrder is { } order && row.StationName is { } name ? new StationRefDto(order, name) : null,
                row.MatchRank switch
                {
                    2 => SearchMatchField.Tag,
                    3 => SearchMatchField.Address,
                    _ => SearchMatchField.Name
                }))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchCuratedItineraryItem>> SearchCuratedItinerariesAsync(
        string keyword,
        int take,
        CancellationToken cancellationToken = default)
    {
        var (prefix, contains) = LikePatterns(keyword);

        // Chỉ lấy lịch trình còn ít nhất một địa điểm Active (cùng luật với danh sách lịch trình mẫu).
        // StationName = ga gần nhất của chặng Active đầu tiên, như CuratedItineraryService.
        return await dbContext.Database.SqlQuery<SearchCuratedItineraryItem>(
            $"""
             SELECT m."Id", m."Title", m."CoverImageUrl", m."EstimatedDurationMinutes",
                    m."EstimatedCostMin", m."EstimatedCostMax", station."Name" AS "StationName"
             FROM (
                 SELECT ci."Id", ci."Title", ci."CoverImageUrl", ci."EstimatedDurationMinutes",
                        ci."EstimatedCostMin", ci."EstimatedCostMax",
                        CASE
                            WHEN unaccent(ci."Title") ILIKE unaccent({prefix}) THEN 0
                            WHEN unaccent(ci."Title") ILIKE unaccent({contains}) THEN 1
                            ELSE 2
                        END AS "MatchRank"
                 FROM "CuratedItineraries" ci
                 WHERE (unaccent(ci."Title") ILIKE unaccent({contains})
                        OR unaccent(COALESCE(ci."Description", '')) ILIKE unaccent({contains}))
                   AND EXISTS (
                       SELECT 1
                       FROM "CuratedItineraryItems" i
                       JOIN "Places" p ON p."Id" = i."PlaceId"
                       WHERE i."CuratedItineraryId" = ci."Id" AND p."Status" = 'Active')
                 ORDER BY "MatchRank", ci."Title", ci."Id"
                 LIMIT {take}
             ) m
             LEFT JOIN LATERAL (
                 SELECT ms."Name"
                 FROM "CuratedItineraryItems" i
                 JOIN "Places" p ON p."Id" = i."PlaceId"
                 CROSS JOIN LATERAL (
                     SELECT s."Name"
                     FROM "MetroStations" s
                     ORDER BY ST_Distance(p."Location"::geography, s."Location"::geography), s."Order"
                     LIMIT 1
                 ) ms
                 WHERE i."CuratedItineraryId" = m."Id" AND p."Status" = 'Active'
                 ORDER BY i."OrderIndex"
                 LIMIT 1
             ) station ON true
             ORDER BY m."MatchRank", m."Title", m."Id"
             """)
            .ToListAsync(cancellationToken);
    }

    // "%" và "_" người dùng gõ là ký tự thường, không phải ký tự đại diện.
    private static (string Prefix, string Contains) LikePatterns(string keyword)
    {
        var escaped = keyword.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return ($"{escaped}%", $"%{escaped}%");
    }

    private sealed class PlaceRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string? ImageUrl { get; init; }
        public decimal EstimatedCostMin { get; init; }
        public decimal EstimatedCostMax { get; init; }
        public int MatchRank { get; init; }
        public int? StationOrder { get; init; }
        public string? StationName { get; init; }
    }
}
