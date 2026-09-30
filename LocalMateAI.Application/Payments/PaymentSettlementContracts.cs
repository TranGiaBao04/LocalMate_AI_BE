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

public sealed record PaymentSettlementResult(PaymentSettlementStatus Status)
{
    public string? TransitionReasonCode { get; init; }
}

public enum PaymentWebhookStatus
{
    Acknowledged,
    InvalidSignature,
    PayloadTooLarge
}

public sealed record PaymentWebhookResult(PaymentWebhookStatus Status);
