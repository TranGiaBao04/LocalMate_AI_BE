using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Payments;

public interface IPaymentSettlementService
{
    Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(VerifiedPaymentNotification notification,
        PaymentTransitionContext context, CancellationToken cancellationToken = default);
    Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default);
}
