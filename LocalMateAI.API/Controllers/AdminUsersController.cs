using FluentValidation;
using FluentValidation.Results;
using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/users")]
[HasPermission(Permissions.ManageUsers)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminUsersController(IAdminUserService adminUserService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminUserListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUsersAsync(
        [FromQuery] AdminUserQuery query,
        [FromServices] IValidator<AdminUserQuery> queryValidator,
        CancellationToken cancellationToken)
    {
        if (GetActor() is not { } actor)
        {
            return InvalidIdentity();
        }

        var validationResult = await queryValidator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(CreateInvalidQueryProblem(validationResult.ToDictionary()));
        }

        return Ok(await adminUserService.GetUsersAsync(query, actor, cancellationToken));
    }

    [HttpGet("filter-options")]
    [ProducesResponseType<AdminUserFilterOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUserFilterOptionsResponse>> GetFilterOptionsAsync(
        CancellationToken cancellationToken) =>
        Ok(await adminUserService.GetFilterOptionsAsync(cancellationToken));

    [HttpGet("{userId:guid}")]
    [ProducesResponseType<AdminUserDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (GetActor() is not { } actor)
        {
            return InvalidIdentity();
        }

        var user = await adminUserService.GetUserAsync(userId, actor, cancellationToken);
        return user is null ? NotFound(CreateUserNotFoundProblem()) : Ok(user);
    }

    [HttpGet("{userId:guid}/payments")]
    [ProducesResponseType<PagedResult<AdminTransactionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentsAsync(
        Guid userId,
        [FromQuery] AdminTransactionQuery query,
        [FromServices] IValidator<AdminTransactionFilterQuery> filterValidator,
        [FromServices] IValidator<PagedQuery> pagingValidator,
        CancellationToken cancellationToken)
    {
        // Cùng luật với GET /api/admin/transactions (status, operationType, createdFrom/To, search, sortBy...).
        var failures = (await filterValidator.ValidateAsync(query, cancellationToken)).Errors
            .Concat((await pagingValidator.ValidateAsync(query.Paging(), cancellationToken)).Errors)
            .ToList();
        if (failures.Count > 0)
        {
            return BadRequest(CreateInvalidQueryProblem(new ValidationResult(failures).ToDictionary()));
        }

        var page = await adminUserService.GetPaymentsAsync(userId, query, cancellationToken);
        return page is null ? NotFound(CreateUserNotFoundProblem()) : Ok(page);
    }

    [HttpGet("{userId:guid}/trips")]
    [ProducesResponseType<PagedResult<AdminUserTripResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTripsAsync(
        Guid userId,
        [FromQuery] AdminUserTripQuery query,
        [FromServices] IValidator<AdminUserTripQuery> queryValidator,
        CancellationToken cancellationToken)
    {
        var validationResult = await queryValidator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(CreateInvalidQueryProblem(validationResult.ToDictionary()));
        }

        var page = await adminUserService.GetTripsAsync(userId, query, cancellationToken);
        return page is null ? NotFound(CreateUserNotFoundProblem()) : Ok(page);
    }

    [HttpPost("{userId:guid}/lock")]
    [ProducesResponseType<AdminUserLockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LockAsync(
        Guid userId,
        [FromBody] LockUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorUserId))
        {
            return InvalidIdentity();
        }

        return ToLockActionResult(await adminUserService.LockAsync(userId, request, actorUserId, cancellationToken));
    }

    [HttpPost("{userId:guid}/unlock")]
    [ProducesResponseType<AdminUserLockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorUserId))
        {
            return InvalidIdentity();
        }

        return ToLockActionResult(await adminUserService.UnlockAsync(userId, actorUserId, cancellationToken));
    }

    private ValidationProblemDetails CreateInvalidQueryProblem(IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "User query is invalid.",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_admin_user_query";
        return problem;
    }

    private IActionResult ToLockActionResult(AdminUserLockResult result) => result.Status switch
    {
        AdminUserLockResultStatus.Success => Ok(result.Response),
        AdminUserLockResultStatus.InvalidReason => BadRequest(CreateInvalidLockReasonProblem()),
        AdminUserLockResultStatus.CannotLockSelf => Conflict(CreateProblem(StatusCodes.Status409Conflict,
            "Cannot lock own account.", "cannot_lock_self", "Không thể tự khoá tài khoản của chính mình.")),
        AdminUserLockResultStatus.UserNotFound => NotFound(CreateUserNotFoundProblem()),
        AdminUserLockResultStatus.CannotManageRoleManager => StatusCode(StatusCodes.Status403Forbidden,
            CreateProblem(StatusCodes.Status403Forbidden, "Cannot lock or unlock a role manager.",
                "cannot_manage_role_manager",
                "Chỉ người có quyền quản lý phân quyền mới được khoá/mở khoá tài khoản có quyền này.")),
        AdminUserLockResultStatus.LastRoleManager => Conflict(CreateProblem(StatusCodes.Status409Conflict,
            "At least one role manager must remain.", "last_role_manager",
            "Phải còn ít nhất một người đang hoạt động có quyền quản lý phân quyền.")),
        _ => throw new InvalidOperationException("Unknown admin user lock result.")
    };

    private ValidationProblemDetails CreateInvalidLockReasonProblem()
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [nameof(LockUserRequest.Reason)] = [$"Reason bắt buộc, tối đa {AdminUserService.MaxLockReasonLength} ký tự."]
        })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Lock reason is invalid.",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_lock_reason";
        return problem;
    }

    private bool TryGetActorId(out Guid actorUserId) =>
        Guid.TryParse(User.FindFirst(AuthClaimNames.Subject)?.Value, out actorUserId) && actorUserId != Guid.Empty;

    // Quyền của người xem lấy từ snapshot AccountAccessMiddleware đã tra cho request này, không truy vấn thêm.
    private AdminActor? GetActor() =>
        TryGetActorId(out var actorUserId)
            ? new AdminActor(actorUserId, HttpContext.GetUserAccess()?.HasPermission(Permissions.ManageRoles) == true)
            : null;

    private IActionResult InvalidIdentity() =>
        Unauthorized(CreateProblem(StatusCodes.Status401Unauthorized,
            "Authentication identity is invalid.", "invalid_identity"));

    private ProblemDetails CreateUserNotFoundProblem() =>
        CreateProblem(StatusCodes.Status404NotFound, "User not found.", "user_not_found");

    private ProblemDetails CreateProblem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = HttpContext.Request.Path };
        problem.Extensions["code"] = code;
        return problem;
    }
}
