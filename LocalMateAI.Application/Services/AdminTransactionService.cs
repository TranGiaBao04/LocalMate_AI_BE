using FluentValidation;
using FluentValidation.Results;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Validators.Payments;

namespace LocalMateAI.Application.Services;

public sealed class AdminTransactionService(IAdminTransactionRepository repository,
    IValidator<AdminTransactionFilterQuery> filterValidator, IValidator<PagedQuery> pagingValidator,
    TimeProvider timeProvider) : IAdminTransactionService
{
    public const int MaxExportRows = 10_000;

    public Task<AdminTransactionDetailResponse?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.GetDetailAsync(id, cancellationToken);

    public async Task<AdminTransactionResult<PagedResult<AdminTransactionResponse>>> GetTransactionsAsync(
        AdminTransactionQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await filterValidator.ValidateAsync(query, cancellationToken);
        var paging = await pagingValidator.ValidateAsync(query.Paging(), cancellationToken);
        var errors = validation.Errors.Concat(paging.Errors).ToArray();
        return errors.Length > 0 ? Invalid<PagedResult<AdminTransactionResponse>>(errors)
            : new(AdminTransactionResultStatus.Success, await repository.GetTransactionsAsync(
                AdminTransactionFilterQueryValidator.Normalize(query), query.Paging(), cancellationToken));
    }

    public async Task<AdminTransactionResult<AdminTransactionSummary>> GetSummaryAsync(AdminTransactionFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = await filterValidator.ValidateAsync(query, cancellationToken);
        return !validation.IsValid ? Invalid<AdminTransactionSummary>(validation.Errors)
            : new(AdminTransactionResultStatus.Success, await repository.GetSummaryAsync(
                AdminTransactionFilterQueryValidator.Normalize(query), cancellationToken));
    }

    public async Task<AdminTransactionResult<AdminTransactionCsvFile>> ExportAsync(AdminTransactionFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = await filterValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid) return Invalid<AdminTransactionCsvFile>(validation.Errors);
        var export = await repository.GetExportRowsAsync(AdminTransactionFilterQueryValidator.Normalize(query),
            MaxExportRows, cancellationToken);
        if (export.MatchingRows > MaxExportRows)
            return new(AdminTransactionResultStatus.ExportLimitExceeded, MatchingRows: export.MatchingRows);
        return new(AdminTransactionResultStatus.Success, new(AdminTransactionCsv.Write(export.Rows),
            $"localmate-transactions-{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd-HHmmss}.csv"));
    }

    private static AdminTransactionResult<T> Invalid<T>(IEnumerable<ValidationFailure> failures) =>
        new(AdminTransactionResultStatus.InvalidQuery, ValidationErrors: failures.GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray()));
}
