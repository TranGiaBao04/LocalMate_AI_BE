using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminTransactionService
{
    Task<AdminTransactionDetailResponse?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminTransactionResult<PagedResult<AdminTransactionResponse>>> GetTransactionsAsync(AdminTransactionQuery query,
        CancellationToken cancellationToken = default);
    Task<AdminTransactionResult<AdminTransactionSummary>> GetSummaryAsync(AdminTransactionFilterQuery query,
        CancellationToken cancellationToken = default);
    Task<AdminTransactionResult<AdminTransactionCsvFile>> ExportAsync(AdminTransactionFilterQuery query,
        CancellationToken cancellationToken = default);
}
