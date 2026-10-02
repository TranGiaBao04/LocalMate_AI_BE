using LocalMateAI.API.Authorization;
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
    [HttpGet("/api/subscriptions/plans")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<SubscriptionPlanResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlanResponse>>> GetPlansAsync(CancellationToken cancellationToken) =>
        Ok(await subscriptionService.GetPlansAsync(cancellationToken));

    [HttpGet("me")]
    [Authorize(Policy = AppPolicies.RegisteredUser)]
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

    [HttpGet("checkout-quote")]
    [Authorize(Policy = AppPolicies.RegisteredUser)]
    [ProducesResponseType<CheckoutQuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CheckoutQuoteResponse>> GetCheckoutQuoteAsync(
        [FromQuery] string? planCode, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(CreateProblem(401, "Authentication identity is invalid.", "invalid_identity"));
        var result = await paymentService.GetCheckoutQuoteAsync(userId, planCode, cancellationToken);
        if (result.Status == PaymentIntentResultStatus.Success) return Ok(result.Response);
        var error = ToPaymentIntentActionResult(new(result.Status));
        return error.Result!;
    }

    [HttpPost("checkout")]
    [Authorize(Policy = AppPolicies.RegisteredUser)]
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
    [Authorize(Policy = AppPolicies.RegisteredUser)]
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
    [Authorize(Policy = AppPolicies.RegisteredUser)]
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
                "A valid active paid plan and current version are required.",
                "invalid_plan_code")),
            PaymentIntentResultStatus.PlanAlreadyActive => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "The requested plan is already active.",
                "plan_already_active")),
            PaymentIntentResultStatus.CoveredByHigherPlan => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "The requested plan is covered by a higher active plan.",
                "already_covered_by_higher_plan")),
            PaymentIntentResultStatus.TargetPlanAlreadyScheduled => Conflict(CreateProblem(
                StatusCodes.Status409Conflict, "The target plan already has a future period.",
                "target_plan_already_scheduled")),
            PaymentIntentResultStatus.UpgradeCheckoutNotReady => Conflict(CreateProblem(
                StatusCodes.Status409Conflict, "Upgrade payment checkout is not enabled in this phase.",
                "upgrade_checkout_not_ready")),
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
        problem.Extensions["type"] = response.Type;
        problem.Extensions["listPrice"] = response.ListPrice;
        problem.Extensions["creditAmount"] = response.CreditAmount;
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
