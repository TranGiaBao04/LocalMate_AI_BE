using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class PaymentWebhookServiceTests
{
    [Fact]
    public async Task InvalidSignature_DoesNotReachSettlement()
    {
        var settlement = new RecordingSettlementService(
            PaymentSettlementStatus.Settled);
        var service = CreateService(
            PaymentWebhookVerificationResult.Invalid(),
            settlement);

        var result = await service.ProcessAsync("{\"signature\":\"invalid\"}");

        Assert.Equal(PaymentWebhookStatus.InvalidSignature, result.Status);
        Assert.Empty(settlement.Notifications);
    }

    [Fact]
    public async Task ValidUnknownOrder_IsAcknowledged()
    {
        var notification = new VerifiedPaymentNotification(40401, 49000, true);
        var settlement = new RecordingSettlementService(
            PaymentSettlementStatus.UnknownOrder);
        var service = CreateService(
            PaymentWebhookVerificationResult.Valid(notification),
            settlement);

        var result = await service.ProcessAsync("verified-payload");

        Assert.Equal(PaymentWebhookStatus.Acknowledged, result.Status);
        Assert.Equal(notification, Assert.Single(settlement.Notifications));
    }

    [Fact]
    public async Task ValidNotification_UsesSharedSettlementAndAcknowledges()
    {
        var notification = new VerifiedPaymentNotification(12001, 59000, true);
        var settlement = new RecordingSettlementService(
            PaymentSettlementStatus.Settled);
        var service = CreateService(
            PaymentWebhookVerificationResult.Valid(notification),
            settlement);

        var result = await service.ProcessAsync("verified-payload");

        Assert.Equal(PaymentWebhookStatus.Acknowledged, result.Status);
        Assert.Equal(notification, Assert.Single(settlement.Notifications));
    }

    private static PaymentWebhookService CreateService(
        PaymentWebhookVerificationResult verification,
        RecordingSettlementService settlement) =>
        new(
            new FakeGateway(verification),
            settlement,
            NullLogger<PaymentWebhookService>.Instance);

    private sealed class FakeGateway(PaymentWebhookVerificationResult verification)
        : IPaymentGateway
    {
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(
            PaymentLinkRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentGatewayOrderResult> GetPaymentAsync(
            long providerOrderCode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(
            string rawPayload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(verification);
    }

    private sealed class RecordingSettlementService(PaymentSettlementStatus status)
        : IPaymentSettlementService
    {
        public List<VerifiedPaymentNotification> Notifications { get; } = [];

        public Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(
            VerifiedPaymentNotification notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.FromResult(new PaymentSettlementResult(status));
        }
    }
}
