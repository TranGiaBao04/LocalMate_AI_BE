using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminCuratedItineraryRepository(AppDbContext dbContext) : IAdminCuratedItineraryRepository
{
    private static readonly SortMap<CuratedItinerary> SortMap =
        new SortMap<CuratedItinerary>("updatedAt", true, itinerary => itinerary.Id)
            .Add("updatedAt", itinerary => itinerary.UpdatedAt)
            .Add("createdAt", itinerary => itinerary.CreatedAt)
            .Add("title", itinerary => itinerary.Title);

    public async Task<PagedResult<AdminCuratedItineraryResponse>> GetPagedAsync(
        AdminCuratedItineraryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var itineraries = dbContext.CuratedItineraries
            .AsNoTracking()
            .Where(itinerary => itinerary.DeletedAt == null);

        if (query.NormalizedSearch is { } search)
        {
            // So khớp không dấu (extension unaccent): "sai gon" tìm ra "Sài Gòn".
            var pattern = LikePattern(search);
            itineraries = itineraries.Where(itinerary =>
                EF.Functions.ILike(EF.Functions.Unaccent(itinerary.Title), EF.Functions.Unaccent(pattern), "\\"));
        }

        var page = await itineraries.ApplySort(query, SortMap).ToPagedResultAsync(query, cancellationToken);
        var items = await ToResponsesAsync(page.Items, cancellationToken);

        return PagedResult<AdminCuratedItineraryResponse>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<AdminCuratedItineraryResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var itinerary = await dbContext.CuratedItineraries
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.DeletedAt == null, cancellationToken);

        return itinerary is null
            ? null
            : (await ToResponsesAsync([itinerary], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<CuratedPlaceCandidate>> GetPlacesAsync(
        IReadOnlyCollection<Guid> placeIds,
        CancellationToken cancellationToken = default)
    {
        var ids = placeIds.ToArray();

        return await dbContext.Places
            .AsNoTracking()
            .Where(place => ids.Contains(place.Id))
            .Select(place => new CuratedPlaceCandidate(
                place.Id,
                place.Name,
                place.Status,
                place.DeletedAt != null,
                place.Category,
                place.Location.Y,
                place.Location.X,
                place.EstimatedCostMin,
                place.EstimatedCostMax))
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> AddAsync(CuratedItineraryDraft draft, CancellationToken cancellationToken = default)
    {
        var itinerary = new CuratedItinerary
        {
            Title = draft.Title,
            Description = draft.Description,
            CoverImageUrl = draft.CoverImageUrl,
            EstimatedDurationMinutes = draft.EstimatedDurationMinutes,
            EstimatedCostMin = draft.EstimatedCostMin,
            EstimatedCostMax = draft.EstimatedCostMax,
            Items = draft.PlaceIds
                .Select((placeId, index) => new CuratedItineraryItem { PlaceId = placeId, OrderIndex = index })
                .ToList()
        };

        dbContext.CuratedItineraries.Add(itinerary);
        await dbContext.SaveChangesAsync(cancellationToken);

        return itinerary.Id;
    }

    public async Task<bool> UpdateAsync(
        Guid id,
        CuratedItineraryDraft draft,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;

        // Lệnh UPDATE này giữ khoá dòng lịch mẫu tới khi commit, nên hai lần sửa đồng thời chạy lần lượt.
        var rowsChanged = await dbContext.CuratedItineraries
            .Where(itinerary => itinerary.Id == id && itinerary.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(itinerary => itinerary.Title, draft.Title)
                .SetProperty(itinerary => itinerary.Description, draft.Description)
                .SetProperty(itinerary => itinerary.CoverImageUrl, draft.CoverImageUrl)
                .SetProperty(itinerary => itinerary.EstimatedDurationMinutes, draft.EstimatedDurationMinutes)
                .SetProperty(itinerary => itinerary.EstimatedCostMin, draft.EstimatedCostMin)
                .SetProperty(itinerary => itinerary.EstimatedCostMax, draft.EstimatedCostMax)
                .SetProperty(itinerary => itinerary.UpdatedAt, now), cancellationToken);

        if (rowsChanged == 0)
        {
            return false;
        }

        // Xoá hết chặng cũ TRƯỚC khi chèn chặng mới: unique index (CuratedItineraryId, OrderIndex) kiểm từng dòng.
        await dbContext.CuratedItineraryItems
            .Where(item => item.CuratedItineraryId == id)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.CuratedItineraryItems.AddRange(draft.PlaceIds.Select((placeId, index) =>
            new CuratedItineraryItem { CuratedItineraryId = id, PlaceId = placeId, OrderIndex = index }));
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Xoá mềm: giữ dòng và các chặng. Trip đã tạo từ lịch mẫu là bản sao nên không bị ảnh hưởng.
        var rowsChanged = await dbContext.CuratedItineraries
            .Where(itinerary => itinerary.Id == id && itinerary.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(itinerary => itinerary.DeletedAt, (DateTime?)now)
                .SetProperty(itinerary => itinerary.UpdatedAt, now), cancellationToken);

        return rowsChanged == 1;
    }

    // Hai truy vấn cho cả trang (chặng + ga của chặng đầu), không truy vấn theo từng lịch.
    private async Task<IReadOnlyList<AdminCuratedItineraryResponse>> ToResponsesAsync(
        IReadOnlyList<CuratedItinerary> itineraries,
        CancellationToken cancellationToken)
    {
        if (itineraries.Count == 0)
        {
            return [];
        }

        var ids = itineraries.Select(itinerary => itinerary.Id).ToArray();

        // Admin thấy mọi chặng, kể cả chặng có địa điểm đang ẩn hoặc đã xoá mềm.
        var stops = await dbContext.CuratedItineraryItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.CuratedItineraryId))
            .OrderBy(item => item.OrderIndex)
            .Select(item => new
            {
                item.CuratedItineraryId,
                item.PlaceId,
                item.Place.Name,
                item.Place.Category,
                item.Place.Status,
                IsDeleted = item.Place.DeletedAt != null,
                item.OrderIndex,
                item.Place.EstimatedCostMin,
                item.Place.EstimatedCostMax
            })
            .ToListAsync(cancellationToken);
        var stopsByItinerary = stops.ToLookup(stop => stop.CuratedItineraryId);

        // Ga gần nhất của chặng đầu (bất kể trạng thái địa điểm); hai ga cách đều thì lấy ga Order nhỏ hơn.
        var stations = await dbContext.Database.SqlQuery<FirstStopStationRow>(
                $"""
                 SELECT f."CuratedItineraryId", s."Name" AS "StationName"
                 FROM (
                     SELECT DISTINCT ON (i."CuratedItineraryId") i."CuratedItineraryId", p."Location"
                     FROM "CuratedItineraryItems" i
                     JOIN "Places" p ON p."Id" = i."PlaceId"
                     WHERE i."CuratedItineraryId" = ANY({ids})
                     ORDER BY i."CuratedItineraryId", i."OrderIndex"
                 ) f
                 CROSS JOIN LATERAL (
                     SELECT ms."Name"
                     FROM "MetroStations" ms
                     ORDER BY ST_Distance(f."Location"::geography, ms."Location"::geography), ms."Order"
                     LIMIT 1
                 ) s
                 """)
            .ToListAsync(cancellationToken);
        var stationByItinerary = stations.ToDictionary(row => row.CuratedItineraryId, row => row.StationName);

        return itineraries
            .Select(itinerary =>
            {
                var itineraryStops = stopsByItinerary[itinerary.Id].ToList();

                return new AdminCuratedItineraryResponse(
                    itinerary.Id,
                    itinerary.Title,
                    itinerary.Description,
                    itinerary.CoverImageUrl,
                    itinerary.EstimatedDurationMinutes,
                    itinerary.EstimatedCostMin,
                    itinerary.EstimatedCostMax,
                    stationByItinerary.GetValueOrDefault(itinerary.Id),
                    itineraryStops.Count(stop => stop.Status != PlaceStatus.Active),
                    itineraryStops
                        .Select(stop => new AdminCuratedItineraryItemResponse(
                            stop.PlaceId,
                            stop.Name,
                            stop.Category.ToString(),
                            stop.Status.ToString(),
                            stop.IsDeleted,
                            stop.OrderIndex,
                            stop.EstimatedCostMin,
                            stop.EstimatedCostMax))
                        .ToList(),
                    itinerary.CreatedAt,
                    itinerary.UpdatedAt);
            })
            .ToList();
    }

    // "%" và "_" người dùng gõ là ký tự thường, không phải ký tự đại diện.
    private static string LikePattern(string search) =>
        "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private sealed record FirstStopStationRow(Guid CuratedItineraryId, string StationName);
}
