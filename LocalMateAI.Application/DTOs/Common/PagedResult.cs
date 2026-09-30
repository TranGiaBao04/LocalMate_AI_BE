namespace LocalMateAI.Application.DTOs.Common;

/// <summary>BE-84: một trang kết quả. <c>TotalPages = 0</c> khi không có dòng nào.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

        // Chia làm tròn lên bằng số nguyên; dùng long để totalCount lớn không tràn.
        var totalPages = (int)((totalCount + (long)pageSize - 1) / pageSize);
        return new PagedResult<T>(items, page, pageSize, totalCount, totalPages);
    }
}
