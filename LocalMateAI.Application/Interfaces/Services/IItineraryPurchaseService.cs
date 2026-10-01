using LocalMateAI.Application.DTOs.ItineraryPurchases;
namespace LocalMateAI.Application.Interfaces.Services;

public interface IItineraryPurchaseService
{
    Task<ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>> CheckoutAsync(Guid userId, Guid attemptId, CancellationToken ct = default);
    Task<ItineraryPurchaseResult<ItineraryPurchaseOrderResponse>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct = default);
    Task<ItineraryPurchaseResult<ItineraryPurchasesMeResponse>> GetMeAsync(Guid userId, CancellationToken ct = default);
    Task<ItineraryPurchaseResult<ItineraryPurchaseAvailabilityResponse>> GetAvailabilityAsync(Guid userId, CancellationToken ct = default);
}
