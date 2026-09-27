namespace LocalMateAI.Application.DTOs.Subscription;

public enum PaymentIntentResultStatus
{
    Success,
    InvalidPlanCode,
    PlanAlreadyActive,
    CoveredByHigherPlan,
    PendingOrderExists,
    NoActiveSubscription,
    GatewayUnavailable,
    NonPersistedUser
}

public sealed record PaymentIntentResult(
    PaymentIntentResultStatus Status,
    PaymentIntentResponse? Response = null);

public enum PaymentOrderLookupStatus
{
    Success,
    NonPersistedUser,
    NotFound
}

public sealed record PaymentOrderLookupResult(
    PaymentOrderLookupStatus Status,
    PaymentOrderResponse? Response = null);
