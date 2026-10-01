using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.ItineraryPurchases;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/itinerary-purchases")]
[Authorize(Policy = AppPolicies.RegisteredUser)]
public sealed class ItineraryPurchasesController(IItineraryPurchaseService service) : ControllerBase
{
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(ItineraryPurchaseCheckoutRequest request, CancellationToken ct) =>
        UserId() is { } user ? Result(await service.CheckoutAsync(user, request.ClientAttemptId, ct)) : Unauthorized();

    [HttpGet("orders/{id:guid}")]
    public async Task<IActionResult> Order(Guid id, CancellationToken ct) =>
        UserId() is { } user ? Result(await service.GetOrderAsync(user, id, ct)) : Unauthorized();

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        UserId() is { } user ? Result(await service.GetMeAsync(user, ct)) : Unauthorized();

    [HttpGet("availability")]
    public async Task<IActionResult> Availability(CancellationToken ct) =>
        UserId() is { } user ? Result(await service.GetAvailabilityAsync(user, ct)) : Unauthorized();

    private Guid? UserId() => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) && id != Guid.Empty ? id : null;
    private IActionResult Result<T>(ItineraryPurchaseResult<T> result) => result.Status switch
    {
        ItineraryPurchaseStatus.Success => Ok(result.Response),
        ItineraryPurchaseStatus.Accepted => StatusCode(202, result.Response),
        ItineraryPurchaseStatus.InvalidRequest => Error(400, "invalid_itinerary_purchase_request"),
        ItineraryPurchaseStatus.NotFound => Error(404, "itinerary_purchase_not_found"),
        ItineraryPurchaseStatus.AccountRequired => Error(403, "persisted_account_required"),
        ItineraryPurchaseStatus.ProviderUnavailable => Error(503, "payment_provider_unavailable"),
        _ => throw new InvalidOperationException("Unknown itinerary purchase result.")
    };
    private ObjectResult Error(int status, string code) => StatusCode(status, new ProblemDetails
    {
        Status = status, Title = code, Extensions = { ["code"] = code }
    });
}
