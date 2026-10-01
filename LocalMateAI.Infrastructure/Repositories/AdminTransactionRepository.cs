using System.Data;
using System.Globalization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminTransactionRepository(AppDbContext context) : IAdminTransactionRepository
{
    private static readonly SortMap<TransactionRow> Sort = new SortMap<TransactionRow>("createdAt", true, r => r.Id)
        .Add("createdAt", r => r.CreatedAt).Add("paidAt", r => r.PaidAt).Add("amount", r => r.Amount)
        .Add("status", r => r.Status).Add("operationType", r => r.OperationType)
        .Add("providerOrderCode", r => r.ProviderOrderCode).Add("userEmail", r => r.UserEmail).Add("planCode", r => r.PlanCode);

    // This is the only projection/filter engine used by list, aggregate and export.
    private IQueryable<TransactionRow> Filtered(AdminTransactionFilter filter)
    {
        var rows = from order in context.PaymentOrders.AsNoTracking()
            join user in context.Users.AsNoTracking() on order.UserId equals user.Id
            join plan in context.SubscriptionPlans.AsNoTracking() on order.PlanId equals (Guid?)plan.Id into plans
            from plan in plans.DefaultIfEmpty()
            select new TransactionRow
            {
                Id = order.Id, ProviderOrderCode = order.ProviderOrderCode, UserId = order.UserId,
                UserFullName = user.FullName, UserEmail = user.Email,
                PlanCode = order.PlanVersionBinding != PlanVersionBinding.Native && order.PlanCode.HasValue
                    ? order.PlanCode.Value.ToString()
                    : plan != null ? plan.Code : order.PlanCode.HasValue ? order.PlanCode.Value.ToString() : null,
                CatalogPlanCode = plan != null ? plan.Code : null,
                PlanName = plan != null ? plan.Name : null, OperationType = order.Type, Status = order.Status,
                Amount = order.Amount, CreatedAt = order.CreatedAt, ExpiresAt = order.ExpiresAt, PaidAt = order.PaidAt
            };
        if (filter.Status is { } status) rows = rows.Where(r => r.Status == status);
        if (filter.OperationType is { } type) rows = rows.Where(r => r.OperationType == type);
        if (filter.CreatedFromUtc is { } from) rows = rows.Where(r => r.CreatedAt >= from);
        if (filter.CreatedToUtc is { } to) rows = rows.Where(r => r.CreatedAt < to);
        if (filter.Search is { } search)
        {
            Guid? id = Guid.TryParse(search, out var parsedId) ? parsedId : null;
            long? code = long.TryParse(search, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCode) ? parsedCode : null;
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            rows = rows.Where(r => (id.HasValue && r.Id == id.Value) || (code.HasValue && r.ProviderOrderCode == code.Value)
                || EF.Functions.ILike(r.UserEmail, pattern, "\\") || EF.Functions.ILike(r.UserFullName, pattern, "\\")
                || (r.PlanCode != null && EF.Functions.ILike(r.PlanCode, pattern, "\\"))
                || (r.CatalogPlanCode != null && EF.Functions.ILike(r.CatalogPlanCode, pattern, "\\"))
                || (r.PlanName != null && EF.Functions.ILike(r.PlanName, pattern, "\\")));
        }
        return rows;
    }

    public async Task<PagedResult<AdminTransactionResponse>> GetTransactionsAsync(AdminTransactionFilter filter, PagedQuery paging,
        CancellationToken cancellationToken = default)
    {
        var page = await Filtered(filter).ApplySort(paging, Sort).ToPagedResultAsync(paging, cancellationToken);
        return PagedResult<AdminTransactionResponse>.Create(page.Items.Select(Response).ToArray(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<AdminTransactionSummary> GetSummaryAsync(AdminTransactionFilter filter,
        CancellationToken cancellationToken = default) =>
        await Filtered(filter).GroupBy(_ => 1).Select(g => new AdminTransactionSummary(g.LongCount(),
            g.LongCount(r => r.Status == PaymentOrderStatus.Paid), g.LongCount(r => r.Status == PaymentOrderStatus.Pending),
            g.LongCount(r => r.Status == PaymentOrderStatus.Failed), g.LongCount(r => r.Status == PaymentOrderStatus.Expired),
            g.Sum(r => r.Status == PaymentOrderStatus.Paid ? r.Amount : 0m), "VND"))
            .SingleOrDefaultAsync(cancellationToken) ?? new(0, 0, 0, 0, 0, 0m, "VND");

    public async Task<AdminTransactionExportRows> GetExportRowsAsync(AdminTransactionFilter filter, int maxRows,
        CancellationToken cancellationToken = default)
    {
        // Count and rows share a read-only snapshot; concurrent checkout/settlement cannot bypass the cap.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = Filtered(filter);
        var count = await query.LongCountAsync(cancellationToken);
        IReadOnlyList<AdminTransactionResponse> rows = count > maxRows ? [] :
            (await Sort.Apply(query, null, null).ToListAsync(cancellationToken)).Select(Response).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return new(count, rows);
    }

    private static AdminTransactionResponse Response(TransactionRow r) => new(r.Id, r.ProviderOrderCode,
        r.UserId, r.UserFullName, r.UserEmail, "SubscriptionPlan", r.PlanCode, r.PlanName,
        r.OperationType.ToString(), r.Status.ToString(), r.Amount, "VND", r.CreatedAt, r.ExpiresAt, r.PaidAt);

    private sealed class TransactionRow
    {
        public Guid Id { get; init; }
        public long ProviderOrderCode { get; init; }
        public Guid UserId { get; init; }
        public string UserFullName { get; init; } = "";
        public string UserEmail { get; init; } = "";
        public string? PlanCode { get; init; }
        public string? CatalogPlanCode { get; init; }
        public string? PlanName { get; init; }
        public PaymentOrderType OperationType { get; init; }
        public PaymentOrderStatus Status { get; init; }
        public decimal Amount { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
        public DateTime? PaidAt { get; init; }
    }
}
