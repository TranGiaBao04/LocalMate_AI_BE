using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPaymentEvidenceRepository
{
    // Commits independently, before starting settlement; this evidence survives settlement rollback.
    Task SaveVerifiedReceiptAsync(PaymentWebhookReceipt receipt, CancellationToken cancellationToken = default);
    Task<int> PurgeExpiredRawPayloadsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
