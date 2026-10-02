using System.Text.Json.Serialization;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.DTOs.ItineraryPurchases;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItineraryPurchaseCheckoutRequest(Guid ClientAttemptId);
public sealed record SingleItineraryEntitlementResponse(Guid EntitlementId, DateTime GrantedAt,
    DateTime? ConsumedAt, Guid? ConsumedTripId, bool Available)
{
    public static SingleItineraryEntitlementResponse From(SingleItineraryEntitlement e) =>
        new(e.Id, e.GrantedAt, e.ConsumedAt, e.ConsumedTripId, e.ConsumedAt is null);
}
public sealed record ItineraryPurchaseOrderResponse(Guid OrderId, string ProductKind, string ProductCode,
    Guid ContractVersionId, string Status, decimal Amount, string Currency, DateTime ExpiresAt, DateTime? PaidAt,
    string? CheckoutUrl, string? QrCode, SingleItineraryEntitlementResponse? Entitlement);
public sealed record ItineraryPurchasesMeResponse(int UnusedEntitlementCount,
    IReadOnlyList<SingleItineraryEntitlementResponse> Entitlements);
public sealed record ItineraryPurchaseAvailabilityResponse(string ProductKind, string ProductCode, string ProductLabel,
    Guid ContractVersionId, decimal Price, string Currency, bool PurchaseAllowed, string? BlockedCode,
    int UnusedEntitlementCount, bool NormalFinalizeAvailable, int NormalSavedTripsUsed, int? NormalSavedTripLimit);
public enum ItineraryPurchaseStatus { Success, Accepted, InvalidRequest, NotFound, AccountRequired, ProviderUnavailable }
public sealed record ItineraryPurchaseResult<T>(ItineraryPurchaseStatus Status, T? Response = default);
