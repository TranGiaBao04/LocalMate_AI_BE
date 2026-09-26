using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/subscription")]
public sealed class SubscriptionController(
    ISubscriptionService subscriptionService,
    IPaymentService paymentService) : ControllerBase
{
    [HttpGet("plans")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<SubscriptionPlanResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SubscriptionPlanResponse>> GetPlans() =>
        Ok(subscriptionService.GetPlans());

    [HttpGet("me")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<SubscriptionMeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SubscriptionMeResponse>> GetMeAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Authentication identity is invalid.",
                "invalid_identity"));
        }

        var response = await subscriptionService.GetMySubscriptionAsync(userId, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                StatusCodes.Status403Forbidden,
                "A persisted user account is required for subscription access.",
                "persisted_account_required"));
        }

        return Ok(response);
    }

    [HttpPost("checkout")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<PaymentIntentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PaymentIntentResponse>> CheckoutAsync(
        [FromBody] CreateCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Authentication identity is invalid.",
                "invalid_identity"));
        }

        var result = await paymentService.CheckoutAsync(
            userId,
            request.PlanCode,
            cancellationToken);
        return ToPaymentIntentActionResult(result);
    }

    [HttpPost("renew")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<PaymentIntentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PaymentIntentResponse>> RenewAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Authentication identity is invalid.",
                "invalid_identity"));
        }

        var result = await paymentService.RenewAsync(userId, cancellationToken);
        return ToPaymentIntentActionResult(result);
    }

    [HttpGet("orders/{orderId}")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<PaymentOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentOrderResponse>> GetOrderAsync(
        string orderId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Authentication identity is invalid.",
                "invalid_identity"));
        }

        if (!Guid.TryParse(orderId, out var parsedOrderId) || parsedOrderId == Guid.Empty)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Payment order ID is invalid.",
                "invalid_id"));
        }

        var result = await paymentService.GetOrderAsync(
            userId,
            parsedOrderId,
            cancellationToken);
        return result.Status switch
        {
            PaymentOrderLookupStatus.Success => Ok(result.Response),
            PaymentOrderLookupStatus.NonPersistedUser => StatusCode(
                StatusCodes.Status403Forbidden,
                CreatePersistedAccountProblem()),
            PaymentOrderLookupStatus.NotFound => NotFound(CreateProblem(
                StatusCodes.Status404NotFound,
                "Payment order was not found.",
                "payment_order_not_found")),
            _ => throw new InvalidOperationException("Unsupported payment order lookup result status.")
        };
    }

    private ActionResult<PaymentIntentResponse> ToPaymentIntentActionResult(
        PaymentIntentResult result) =>
        result.Status switch
        {
            PaymentIntentResultStatus.Success => StatusCode(
                StatusCodes.Status201Created,
                result.Response),
            PaymentIntentResultStatus.InvalidPlanCode => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Plan code must be TripPass or Membership.",
                "invalid_plan_code")),
            PaymentIntentResultStatus.PlanAlreadyActive => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "The requested plan is already active.",
                "plan_already_active")),
            PaymentIntentResultStatus.CoveredByHigherPlan => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "The requested plan is covered by a higher active plan.",
                "already_covered_by_higher_plan")),
            PaymentIntentResultStatus.PendingOrderExists => Conflict(
                CreatePendingOrderProblem(result.Response!)),
            PaymentIntentResultStatus.NoActiveSubscription => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "No active paid subscription is available to renew.",
                "no_active_subscription")),
            PaymentIntentResultStatus.GatewayUnavailable => StatusCode(
                StatusCodes.Status502BadGateway,
                CreateProblem(
                    StatusCodes.Status502BadGateway,
                    "The payment gateway is currently unavailable.",
                    "payment_gateway_unavailable")),
            PaymentIntentResultStatus.NonPersistedUser => StatusCode(
                StatusCodes.Status403Forbidden,
                CreatePersistedAccountProblem()),
            _ => throw new InvalidOperationException("Unsupported payment intent result status.")
        };

    private bool TryGetUserId(out Guid userId)
    {
        var subject = User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId) && userId != Guid.Empty;
    }

    private ProblemDetails CreatePersistedAccountProblem() => CreateProblem(
        StatusCodes.Status403Forbidden,
        "A persisted user account is required for subscription access.",
        "persisted_account_required");

    private ProblemDetails CreatePendingOrderProblem(PaymentIntentResponse response)
    {
        var problem = CreateProblem(
            StatusCodes.Status409Conflict,
            "A reusable pending payment order already exists.",
            "pending_order_exists");
        problem.Extensions["orderId"] = response.OrderId;
        problem.Extensions["qrCode"] = response.QrCode;
        problem.Extensions["checkoutUrl"] = response.CheckoutUrl;
        problem.Extensions["amount"] = response.Amount;
        problem.Extensions["expiresAt"] = response.ExpiresAt;
        return problem;
    }

    private ProblemDetails CreateProblem(int status, string title, string code)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.com/{status}",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        return problem;
    }
}
