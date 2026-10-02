namespace LocalMateAI.Application.DTOs.Subscription;

public sealed record CheckoutQuoteResponse(
    string PlanCode, string Type, decimal ListPrice, decimal CreditAmount,
    decimal Amount, int DurationDays, IReadOnlyList<CheckoutQuoteCreditResponse> Credits);

public sealed record CheckoutQuoteCreditResponse(
    string PlanCode, string PlanName, int RemainingDays, decimal CreditAmount);

public sealed record CheckoutQuoteResult(
    PaymentIntentResultStatus Status, CheckoutQuoteResponse? Response = null);
