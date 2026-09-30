using LocalMateAI.Application.DTOs.Common;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Persistence.Querying;

/// <summary>BE-84: phân trang/sắp xếp dùng chung cho repository của các API danh sách admin.</summary>
public static class QueryableExtensions
{
    public static IOrderedQueryable<T> ApplySort<T>(
        this IQueryable<T> query,
        PagedQuery pagedQuery,
        SortMap<T> sortMap)
    {
        ArgumentNullException.ThrowIfNull(pagedQuery);
        ArgumentNullException.ThrowIfNull(sortMap);

        return sortMap.Apply(query, pagedQuery.SortBy, pagedQuery.SortDirection);
    }

    /// <summary>
    /// Đếm tổng rồi lấy đúng một trang. Gọi SAU <see cref="ApplySort{T}"/> (và sau <c>Select</c> nếu chiếu sang DTO)
    /// để các trang không lặp/sót dòng. Đếm và lấy trang là 2 truy vấn riêng: dữ liệu đổi giữa chừng có thể lệch 1 dòng.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedQuery pagedQuery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(pagedQuery);
        ArgumentOutOfRangeException.ThrowIfLessThan(pagedQuery.Page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pagedQuery.Page, PagedQuery.MaxPage);
        ArgumentOutOfRangeException.ThrowIfLessThan(pagedQuery.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pagedQuery.PageSize, PagedQuery.MaxPageSize);

        var totalCount = await query.CountAsync(cancellationToken);

        // Page ≤ 10.000 và PageSize ≤ 100 nên offset tối đa ~1 triệu, không tràn int.
        var skip = (pagedQuery.Page - 1) * pagedQuery.PageSize;
        IReadOnlyList<T> items = skip >= totalCount
            ? []
            : await query.Skip(skip).Take(pagedQuery.PageSize).ToListAsync(cancellationToken);

        return PagedResult<T>.Create(items, pagedQuery.Page, pagedQuery.PageSize, totalCount);
    }
}
