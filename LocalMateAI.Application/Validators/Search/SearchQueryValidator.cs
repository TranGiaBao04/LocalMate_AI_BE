using FluentValidation;
using LocalMateAI.Application.DTOs.Search;

namespace LocalMateAI.Application.Validators.Search;

public sealed class SearchQueryValidator : AbstractValidator<SearchQuery>
{
    public SearchQueryValidator()
    {
        // Độ dài tính trên từ khoá đã chuẩn hoá, lỗi vẫn báo về field Q.
        RuleFor(query => query.Q)
            .Must((query, _) => query.Keyword is { Length: >= SearchQuery.MinKeywordLength })
            .WithMessage($"Từ khoá phải có ít nhất {SearchQuery.MinKeywordLength} ký tự.")
            .Must((query, _) => query.Keyword is not { Length: > SearchQuery.MaxKeywordLength })
            .WithMessage($"Từ khoá tối đa {SearchQuery.MaxKeywordLength} ký tự.");

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, SearchQuery.MaxLimit)
            .WithMessage($"Limit phải từ 1 đến {SearchQuery.MaxLimit}.");
    }
}
