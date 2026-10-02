using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Payments;

// Purchased evidence, not the plan's current commercial contract.
public sealed record SubscriptionQuoteSource(
    SubscriptionPeriod Period,
    SubscriptionPlan Plan,
    SubscriptionPlanVersion? PurchasedVersion,
    PaymentOrder? SourceOrder);
