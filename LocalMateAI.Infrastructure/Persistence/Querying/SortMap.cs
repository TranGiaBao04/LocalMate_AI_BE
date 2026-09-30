using System.Linq.Expressions;
using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Infrastructure.Persistence.Querying;

/// <summary>
/// BE-84: whitelist "tên sortBy → biểu thức sắp xếp" của một endpoint. <c>sortBy</c> chỉ được ánh xạ qua bảng này,
/// không bao giờ ghép chuỗi vào SQL. Tên trường phải khớp danh sách <c>sortBy</c> mà validator của endpoint cho phép.
/// Luôn sắp thêm theo <c>tieBreaker</c> (thường là <c>Id</c>) để các dòng cùng giá trị có thứ tự ổn định giữa các trang.
/// </summary>
public sealed class SortMap<T>
{
    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> fields =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Expression<Func<T, Guid>> tieBreaker;

    public SortMap(string defaultField, bool defaultDescending, Expression<Func<T, Guid>> tieBreaker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultField);
        ArgumentNullException.ThrowIfNull(tieBreaker);

        DefaultField = defaultField;
        DefaultDescending = defaultDescending;
        this.tieBreaker = tieBreaker;
    }

    public string DefaultField { get; }

    public bool DefaultDescending { get; }

    public IReadOnlyCollection<string> FieldNames => fields.Keys;

    public SortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> keySelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(keySelector);

        var added = fields.TryAdd(
            name,
            (query, descending) => descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector));
        if (!added)
        {
            throw new ArgumentException($"Trường sắp xếp '{name}' đã được khai báo.", nameof(name));
        }

        return this;
    }

    /// <summary>
    /// Sắp theo <paramref name="sortBy"/>/<paramref name="sortDirection"/> (bỏ trống ⇒ mặc định). Giá trị sai ném
    /// <see cref="ArgumentException"/>: validator đã chặn trước, tới được đây nghĩa là lỗi lập trình (500).
    /// </summary>
    public IOrderedQueryable<T> Apply(IQueryable<T> query, string? sortBy, string? sortDirection)
    {
        ArgumentNullException.ThrowIfNull(query);

        var field = string.IsNullOrWhiteSpace(sortBy) ? DefaultField : sortBy.Trim();
        if (!fields.TryGetValue(field, out var orderBy))
        {
            throw new ArgumentException($"Trường sắp xếp '{field}' không có trong danh sách cho phép.", nameof(sortBy));
        }

        return orderBy(query, IsDescending(sortDirection)).ThenBy(tieBreaker);
    }

    private bool IsDescending(string? sortDirection)
    {
        if (string.IsNullOrWhiteSpace(sortDirection))
        {
            return DefaultDescending;
        }

        var direction = sortDirection.Trim();
        if (string.Equals(direction, PagedQuery.Descending, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(direction, PagedQuery.Ascending, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new ArgumentException($"Chiều sắp xếp '{direction}' không hợp lệ.", nameof(sortDirection));
    }
}
