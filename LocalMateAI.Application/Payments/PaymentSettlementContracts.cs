namespace LocalMateAI.Application.Payments;

public enum PaymentSettlementStatus
{
    Settled,
    AlreadyPaid,
    NonSuccessful,
    AmountMismatch,
    UnknownOrder,
    InvalidPaidPlan,
    UnresolvedPlanVersion
}

public sealed record PaymentSettlementResult(PaymentSettlementStatus Status);

public enum PaymentWebhookStatus
{
    Acknowledged,
    InvalidSignature
}

public sealed record PaymentWebhookResult(PaymentWebhookStatus Status);
