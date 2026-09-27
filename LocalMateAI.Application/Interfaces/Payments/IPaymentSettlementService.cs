using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Payments;

public interface IPaymentSettlementService
{
    Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default);
}
