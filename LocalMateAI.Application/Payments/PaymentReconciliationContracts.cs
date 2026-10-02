using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Payments;

public enum PaymentReconciliationStatus
{
    Reconciled,
    NoChange,
    AlreadyPaid,
    ProviderUnavailable,
    ProviderMismatch,
    NotFound
}

public sealed record PaymentReconciliationResult(
    PaymentReconciliationStatus Status, Guid OrderId, long? ProviderOrderCode, bool ProviderChecked,
    PaymentGatewayOrderStatus? ProviderStatus, PaymentOrderStatus? LocalStatusBefore,
    PaymentOrderStatus? LocalStatusAfter, bool StatusChanged, DateTime CheckedAt,
    PaymentSettlementStatus? SettlementStatus = null);

public sealed class PaymentReconciliationOptions
{
    public const string SectionName = "PaymentReconciliation";
    public int BatchSize { get; set; } = 100;
    public int PollIntervalSeconds { get; set; } = 60;
}
