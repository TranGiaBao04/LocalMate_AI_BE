using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminTransactionRepository
{
    Task<PagedResult<AdminTransactionResponse>> GetTransactionsAsync(AdminTransactionFilter filter, PagedQuery paging,
        CancellationToken cancellationToken = default);
    Task<AdminTransactionSummary> GetSummaryAsync(AdminTransactionFilter filter, CancellationToken cancellationToken = default);
    Task<AdminTransactionExportRows> GetExportRowsAsync(AdminTransactionFilter filter, int maxRows,
        CancellationToken cancellationToken = default);
}
