using FluentValidation;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.AdminAiUsage;

public sealed class AdminAiCallsRequestValidator : AbstractValidator<AdminAiCallsRequest>
{
    private static readonly PagingValidator Paging = new();
    private static readonly string[] Kinds = ["ParseRequest", "Explain"];
    private static readonly string[] Statuses = ["Succeeded", "ProviderFailed", "InvalidOutput", "Abandoned", "InProgress"];

    public AdminAiCallsRequestValidator()
    {
        RuleFor(query => query).Custom((query, context) =>
        {
            foreach (var error in Paging.Validate(query with { Search = null }).Errors) context.AddFailure(error);
        });
        RuleFor(query => query.From).Must(IsDate).WithMessage("From must have format yyyy-MM-dd.");
        RuleFor(query => query.To).Must(IsDate).WithMessage("To must have format yyyy-MM-dd.");
        RuleFor(query => query.From)
            .Must((query, _) => !DashboardDateRules.TryParseDate(query.From, out var from)
                || !DashboardDateRules.TryParseDate(query.To, out var to) || from <= to)
            .WithMessage("From must not exceed To.");
        RuleFor(query => query.Kind).Must(value => IsChoice(value, Kinds)).WithMessage("Kind must be ParseRequest or Explain.");
        RuleFor(query => query.Status).Must(value => IsChoice(value, Statuses)).WithMessage("Status is not supported.");
    }

    private static bool IsDate(string? value) => string.IsNullOrWhiteSpace(value) || DashboardDateRules.TryParseDate(value, out _);
    private static bool IsChoice(string? value, string[] choices) => string.IsNullOrWhiteSpace(value)
        || choices.Contains(value.Trim(), StringComparer.Ordinal);

    private sealed class PagingValidator() : PagedQueryValidator<AdminAiCallsRequest>(["startedAt"]);
}
