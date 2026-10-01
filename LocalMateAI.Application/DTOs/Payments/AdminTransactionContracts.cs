using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Payments;

public record AdminTransactionFilterQuery
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? OperationType { get; init; }
    public string? CreatedFrom { get; init; }
    public string? CreatedTo { get; init; }
}

public sealed record AdminTransactionQuery : AdminTransactionFilterQuery
{
    public static readonly IReadOnlyCollection<string> SortFields =
        ["createdAt", "paidAt", "amount", "status", "operationType", "providerOrderCode", "userEmail", "planCode"];
    public int Page { get; init; } = PagedQuery.DefaultPage;
    public int PageSize { get; init; } = PagedQuery.DefaultPageSize;
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public PagedQuery Paging() => new()
    { Page = Page, PageSize = PageSize, SortBy = SortBy, SortDirection = SortDirection, Search = Search };
}

public sealed record AdminTransactionFilter(string? Search, PaymentOrderStatus? Status,
    PaymentOrderType? OperationType, DateTime? CreatedFromUtc, DateTime? CreatedToUtc);

public sealed record AdminTransactionResponse(Guid Id, long ProviderOrderCode,
    Guid UserId, string UserFullName, string UserEmail, string ProductKind,
    string? PlanCode, string? PlanName, string OperationType, string Status,
    decimal Amount, string Currency, DateTime CreatedAt, DateTime ExpiresAt, DateTime? PaidAt);

public sealed record AdminTransactionSummary(long TotalTransactions, long PaidCount, long PendingCount,
    long FailedCount, long ExpiredCount, decimal GrossRevenue, string Currency);

public sealed record AdminTransactionExportRows(long MatchingRows, IReadOnlyList<AdminTransactionResponse> Rows);
public sealed record AdminTransactionCsvFile(byte[] Content, string FileName);
public enum AdminTransactionResultStatus { Success, InvalidQuery, ExportLimitExceeded }
public sealed record AdminTransactionResult<T>(AdminTransactionResultStatus Status, T? Response = default,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null, long? MatchingRows = null);
