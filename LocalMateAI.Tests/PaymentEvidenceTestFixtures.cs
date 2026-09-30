using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

internal sealed class PaymentEvidenceTestRepository : IPaymentEvidenceRepository
{
    public List<PaymentWebhookReceipt> Receipts { get; } = [];
    public Task SaveVerifiedReceiptAsync(PaymentWebhookReceipt receipt, CancellationToken cancellationToken = default)
    {
        Receipts.Add(receipt);
        return Task.CompletedTask;
    }
    public Task<int> PurgeExpiredRawPayloadsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var due = Receipts.Where(r => r.RawPayload is not null && r.RawPayloadRetainUntil <= nowUtc).ToArray();
        foreach (var receipt in due) { receipt.RawPayload = null; receipt.RawPayloadPurgedAt = nowUtc; }
        return Task.FromResult(due.Length);
    }
}

internal sealed class PaymentEvidenceTestGateway : IPaymentGateway
{
    public PaymentWebhookVerificationResult Verification { get; set; } = PaymentWebhookVerificationResult.Invalid();
    public PaymentLinkResult Link { get; set; } = PaymentLinkResult.Unavailable();
    public PaymentGatewayOrderResult? Lookup { get; set; }
    public int VerifyCalls { get; private set; }
    public string? VerifiedBody { get; private set; }
    public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string rawPayload, CancellationToken cancellationToken = default)
    { VerifyCalls++; VerifiedBody = rawPayload; return Task.FromResult(Verification); }
    public Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Link);
    public Task<PaymentGatewayOrderResult> GetPaymentAsync(long providerOrderCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Lookup ?? PaymentGatewayOrderResult.Unavailable(providerOrderCode));
}

internal sealed class PaymentEvidenceTestClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => new(Now);
}

internal sealed class PaymentEvidenceTestSettlement : IPaymentSettlementService
{
    public List<PaymentTransitionContext> Contexts { get; } = [];
    public bool Throw { get; set; }
    public Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(VerifiedPaymentNotification notification, CancellationToken cancellationToken = default) =>
        ApplyVerifiedPaymentAsync(notification, new(LocalMateAI.Domain.Enums.PaymentStatusChangeSource.ProviderLookup), cancellationToken);
    public Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(VerifiedPaymentNotification notification,
        PaymentTransitionContext context, CancellationToken cancellationToken = default)
    {
        Contexts.Add(context);
        if (Throw) throw new InvalidOperationException("Controlled settlement failure.");
        return Task.FromResult(new PaymentSettlementResult(PaymentSettlementStatus.Settled));
    }
}
