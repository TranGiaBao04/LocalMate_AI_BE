namespace LocalMateAI.Application.DTOs.Common;

/// <summary>
/// BE-84: tham số phân trang/sắp xếp/tìm kiếm dùng chung cho các API danh sách <c>/api/admin/*</c>
/// (nhận qua <c>[FromQuery]</c>). Endpoint cần bộ lọc riêng thì khai báo record kế thừa lớp này,
/// kèm danh sách <c>sortBy</c> hợp lệ và một validator kế thừa <c>PagedQueryValidator&lt;T&gt;</c>.
/// </summary>
public record PagedQuery
{
    public const int DefaultPage = 1;
    public const int MaxPage = 10_000;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;
    public const string Ascending = "asc";
    public const string Descending = "desc";

    public int Page { get; init; } = DefaultPage;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Tên trường sắp xếp (không phân biệt hoa thường); bỏ trống ⇒ mặc định của endpoint.</summary>
    public string? SortBy { get; init; }

    /// <summary><c>asc</c> hoặc <c>desc</c> (không phân biệt hoa thường); bỏ trống ⇒ mặc định của endpoint.</summary>
    public string? SortDirection { get; init; }

    public string? Search { get; init; }

    /// <summary>Từ khoá đã cắt khoảng trắng; rỗng hoặc toàn khoảng trắng ⇒ <c>null</c> (không tìm).</summary>
    public string? NormalizedSearch => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}
