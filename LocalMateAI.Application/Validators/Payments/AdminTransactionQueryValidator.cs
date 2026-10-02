using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Validators.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Validators.Payments;

public sealed class AdminTransactionFilterQueryValidator : AbstractValidator<AdminTransactionFilterQuery>
{
    private static readonly Regex AbsoluteTimestamp = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?(?:Z|[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public AdminTransactionFilterQueryValidator()
    {
        RuleFor(q => q.Search).Must(s => s is null || s.Trim().Length <= PagedQuery.MaxSearchLength)
            .WithMessage($"Search must not exceed {PagedQuery.MaxSearchLength} characters.");
        RuleFor(q => q.Status).Must(s => s is null || ParseEnum<PaymentOrderStatus>(s) is not null)
            .WithMessage("Status must be Pending, Paid, Failed, Expired or ReviewRequired.");
        RuleFor(q => q.OperationType).Must(s => s is null || ParseEnum<PaymentOrderType>(s) is not null)
            .WithMessage("OperationType must be Purchase, Renewal or Upgrade.");
        RuleFor(q => q.CreatedFrom).Must(s => s is null || ParseTimestamp(s) is not null)
            .WithMessage("CreatedFrom must be an ISO timestamp with Z or an explicit UTC offset.");
        RuleFor(q => q.CreatedTo).Must(s => s is null || ParseTimestamp(s) is not null)
            .WithMessage("CreatedTo must be an ISO timestamp with Z or an explicit UTC offset.");
        RuleFor(q => q).Must(q => ParseTimestamp(q.CreatedFrom) is not { } from
            || ParseTimestamp(q.CreatedTo) is not { } to || from < to)
            .WithMessage("CreatedFrom must precede CreatedTo.").OverridePropertyName("createdTo");
    }

    // Numeric enum strings are not public filter values, even when Enum.TryParse accepts them.
    public static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<T>(name) : null;

    public static DateTime? ParseTimestamp(string? value) => value is not null
        && AbsoluteTimestamp.IsMatch(value.Trim())
        && DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            ? timestamp.UtcDateTime : null;

    public static AdminTransactionFilter Normalize(AdminTransactionFilterQuery query) => new(
        string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
        ParseEnum<PaymentOrderStatus>(query.Status), ParseEnum<PaymentOrderType>(query.OperationType),
        ParseTimestamp(query.CreatedFrom), ParseTimestamp(query.CreatedTo));
}

public sealed class AdminTransactionPagingValidator() : PagedQueryValidator<PagedQuery>(AdminTransactionQuery.SortFields);
