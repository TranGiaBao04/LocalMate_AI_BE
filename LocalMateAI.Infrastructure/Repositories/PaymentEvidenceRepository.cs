using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentEvidenceRepository(AppDbContext context) : IPaymentEvidenceRepository
{
    public const int PurgeBatchSize = 500;

    public async Task SaveVerifiedReceiptAsync(PaymentWebhookReceipt receipt, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Verified receipt must commit before settlement starts.");
        receipt.PaymentOrderId = await context.PaymentOrders.AsNoTracking()
            .Where(o => o.ProviderOrderCode == receipt.ProviderOrderCode).Select(o => (Guid?)o.Id)
            .SingleOrDefaultAsync(cancellationToken);
        context.PaymentWebhookReceipts.Add(receipt);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> PurgeExpiredRawPayloadsAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        context.PaymentWebhookReceipts.Where(r => r.RawPayload != null && r.RawPayloadRetainUntil <= nowUtc)
            .OrderBy(r => r.RawPayloadRetainUntil).ThenBy(r => r.Id).Take(PurgeBatchSize)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RawPayload, (string?)null)
                .SetProperty(r => r.RawPayloadPurgedAt, nowUtc), cancellationToken);
}
