using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Payments;

public interface IPaymentReconciliationService
{
    Task<PaymentReconciliationResult> ReconcileAsync(Guid orderId, PaymentTransitionContext context,
        CancellationToken cancellationToken = default);
}
