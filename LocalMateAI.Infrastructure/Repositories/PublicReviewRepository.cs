using LocalMateAI.Application.DTOs.PublicReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PublicReviewRepository(AppDbContext context) : IPublicReviewRepository
{
    public async Task<IReadOnlyList<PublicReviewItem>> GetFeaturedAsync(
        int minRating,
        int take,
        CancellationToken cancellationToken = default)
    {
        // DISTINCT ON giữ đánh giá mới nhất của mỗi người; điều kiện địa điểm khớp IsPlaceVisibleAsync.
        var rows = await context.Database.SqlQuery<FeaturedReviewSqlRow>($"""
            SELECT latest."Id", latest."Rating", latest."Comment", latest."QuickTags", latest."CreatedAt",
                   latest."ReviewerName", latest."PlaceId", latest."PlaceName"
            FROM (
                SELECT DISTINCT ON (r."UserId")
                       r."Id", r."Rating", r."Comment", r."QuickTags", r."CreatedAt",
                       u."FullName" AS "ReviewerName", p."Id" AS "PlaceId", p."Name" AS "PlaceName"
                FROM "PlaceReviews" r
                JOIN "Places" p ON p."Id" = r."PlaceId"
                JOIN "Users" u ON u."Id" = r."UserId"
                WHERE r."Rating" >= {minRating}
                  AND btrim(COALESCE(r."Comment", '')) <> ''
                  AND p."Status" = 'Active'
                  AND p."DeletedAt" IS NULL
                ORDER BY r."UserId", r."CreatedAt" DESC, r."Id" DESC
            ) latest
            ORDER BY latest."CreatedAt" DESC, latest."Id" DESC
            LIMIT {take}
            """).ToListAsync(cancellationToken);

        return rows
            .Select(row => new PublicReviewItem(
                row.Id,
                row.Rating,
                row.Comment,
                row.QuickTags,
                row.CreatedAt,
                row.ReviewerName,
                new PublicReviewPlace(row.PlaceId, row.PlaceName)))
            .ToList();
    }

    private sealed class FeaturedReviewSqlRow
    {
        public Guid Id { get; init; }
        public int Rating { get; init; }
        public string Comment { get; init; } = string.Empty;
        public string[] QuickTags { get; init; } = [];
        public DateTime CreatedAt { get; init; }
        public string ReviewerName { get; init; } = string.Empty;
        public Guid PlaceId { get; init; }
        public string PlaceName { get; init; } = string.Empty;
    }
}
