using FluentValidation;
using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.Validators.Common;

/// <summary>
/// BE-84: luật chung cho <see cref="PagedQuery"/>. Validator của từng endpoint kế thừa lớp này, truyền danh sách
/// <c>sortBy</c> hợp lệ (phải khớp tên đã khai báo trong <c>SortMap</c> ở Infrastructure) rồi thêm luật cho bộ lọc riêng.
/// Lớp abstract nên <c>AddValidatorsFromAssemblyContaining</c> không đăng ký nó; lớp con cụ thể tự được đăng ký.
/// </summary>
public abstract class PagedQueryValidator<TQuery> : AbstractValidator<TQuery>
    where TQuery : PagedQuery
{
    protected PagedQueryValidator(IReadOnlyCollection<string> sortFields)
    {
        ArgumentNullException.ThrowIfNull(sortFields);
        if (sortFields.Count == 0)
        {
            throw new ArgumentException("Cần ít nhất một trường sắp xếp.", nameof(sortFields));
        }

        var allowedSortFields = string.Join(", ", sortFields);

        RuleFor(query => query.Page)
            .InclusiveBetween(1, PagedQuery.MaxPage)
            .WithMessage($"Page phải từ 1 đến {PagedQuery.MaxPage}.");
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, PagedQuery.MaxPageSize)
            .WithMessage($"PageSize phải từ 1 đến {PagedQuery.MaxPageSize}.");
        RuleFor(query => query.SortBy)
            .Must(sortBy => string.IsNullOrWhiteSpace(sortBy)
                || sortFields.Contains(sortBy.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"SortBy phải là một trong: {allowedSortFields}.");
        RuleFor(query => query.SortDirection)
            .Must(IsValidDirection)
            .WithMessage($"SortDirection phải là '{PagedQuery.Ascending}' hoặc '{PagedQuery.Descending}'.");
        RuleFor(query => query.Search)
            .Must(search => search is null || search.Trim().Length <= PagedQuery.MaxSearchLength)
            .WithMessage($"Search tối đa {PagedQuery.MaxSearchLength} ký tự.");
    }

    private static bool IsValidDirection(string? direction) =>
        string.IsNullOrWhiteSpace(direction)
        || string.Equals(direction.Trim(), PagedQuery.Ascending, StringComparison.OrdinalIgnoreCase)
        || string.Equals(direction.Trim(), PagedQuery.Descending, StringComparison.OrdinalIgnoreCase);
}
