using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Roles;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin")]
[HasPermission(Permissions.ManageRoles)]
public sealed class AdminRolesController(IAdminRoleService adminRoleService) : ControllerBase
{
    private const string GetRoleRouteName = "GetAdminRoleById";

    [HttpGet("permissions")]
    [ProducesResponseType<IReadOnlyList<PermissionResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PermissionResponse>> GetPermissions() =>
        Ok(adminRoleService.GetPermissions());

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<AdminRoleResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminRoleResponse>>> GetRolesAsync(CancellationToken cancellationToken) =>
        Ok(await adminRoleService.GetRolesAsync(cancellationToken));

    [HttpGet("roles/{roleId:guid}", Name = GetRoleRouteName)]
    [ProducesResponseType<AdminRoleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminRoleResponse>> GetRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await adminRoleService.GetRoleAsync(roleId, cancellationToken);
        return role is null ? NotFound(RoleNotFoundProblem()) : Ok(role);
    }

    [HttpPost("roles")]
    [ProducesResponseType<AdminRoleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRoleAsync(
        [FromBody] SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await adminRoleService.CreateRoleAsync(request, cancellationToken);
        return result.Status == AdminRoleResultStatus.Success
            ? CreatedAtRoute(GetRoleRouteName, new { roleId = result.Response!.Id }, result.Response)
            : ToProblem(result);
    }

    [HttpPut("roles/{roleId:guid}")]
    [ProducesResponseType<AdminRoleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateRoleAsync(
        Guid roleId,
        [FromBody] SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorUserId))
        {
            return InvalidIdentity();
        }

        var result = await adminRoleService.UpdateRoleAsync(roleId, request, actorUserId, cancellationToken);
        return result.Status == AdminRoleResultStatus.Success ? Ok(result.Response) : ToProblem(result);
    }

    [HttpDelete("roles/{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var result = await adminRoleService.DeleteRoleAsync(roleId, cancellationToken);
        return result.Status == AdminRoleResultStatus.Success ? NoContent() : ToProblem(result);
    }

    [HttpPut("users/{userId:guid}/role")]
    [ProducesResponseType<UserRoleAssignmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignUserRoleAsync(
        Guid userId,
        [FromBody] AssignUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorUserId))
        {
            return InvalidIdentity();
        }

        var result = await adminRoleService.AssignUserRoleAsync(userId, request, actorUserId, cancellationToken);
        return result.Status switch
        {
            AssignUserRoleResultStatus.Success => Ok(result.Response),
            AssignUserRoleResultStatus.InvalidRole =>
                BadRequest(Problem(StatusCodes.Status400BadRequest, "Role is invalid.", "invalid_role",
                    "Role không tồn tại.")),
            AssignUserRoleResultStatus.UserNotFound =>
                NotFound(Problem(StatusCodes.Status404NotFound, "User was not found.", "user_not_found")),
            AssignUserRoleResultStatus.CannotChangeOwnRole =>
                Conflict(Problem(StatusCodes.Status409Conflict, "Cannot change own role.", "cannot_change_own_role",
                    "Không thể tự đổi role của chính mình.")),
            AssignUserRoleResultStatus.LastRoleManager =>
                Conflict(LastRoleManagerProblem()),
            _ => throw new InvalidOperationException("Unknown assign user role result.")
        };
    }

    private IActionResult ToProblem(AdminRoleResult result) => result.Status switch
    {
        AdminRoleResultStatus.ValidationFailed => BadRequest(InvalidRoleDataProblem(result.ValidationErrors!)),
        AdminRoleResultStatus.NotFound => NotFound(RoleNotFoundProblem()),
        AdminRoleResultStatus.NameExists =>
            Conflict(Problem(StatusCodes.Status409Conflict, "Role name already exists.", "role_name_exists",
                "Tên role đã tồn tại.")),
        AdminRoleResultStatus.SystemRoleLocked =>
            Conflict(Problem(StatusCodes.Status409Conflict, "System role cannot be changed.", "system_role_locked",
                "Không thể sửa hoặc xoá role hệ thống.")),
        AdminRoleResultStatus.RoleInUse =>
            Conflict(Problem(StatusCodes.Status409Conflict, "Role is in use.", "role_in_use",
                "Role đang có người dùng, hãy chuyển họ sang role khác trước.")),
        AdminRoleResultStatus.LastRoleManager => Conflict(LastRoleManagerProblem()),
        AdminRoleResultStatus.CannotRemoveOwnRoleManager =>
            Conflict(Problem(StatusCodes.Status409Conflict, "Cannot remove own role management.",
                "cannot_remove_own_role_manager",
                "Không thể bỏ quyền quản lý phân quyền khỏi role bạn đang giữ.")),
        _ => throw new InvalidOperationException("Unknown admin role result.")
    };

    private bool TryGetActorId(out Guid actorUserId) =>
        Guid.TryParse(User.FindFirst(AuthClaimNames.Subject)?.Value, out actorUserId) && actorUserId != Guid.Empty;

    private IActionResult InvalidIdentity() =>
        Unauthorized(Problem(StatusCodes.Status401Unauthorized, "Authentication identity is invalid.", "invalid_identity"));

    private ProblemDetails RoleNotFoundProblem() =>
        Problem(StatusCodes.Status404NotFound, "Role was not found.", "role_not_found");

    private ProblemDetails LastRoleManagerProblem() =>
        Problem(StatusCodes.Status409Conflict, "At least one role manager must remain.", "last_role_manager",
            "Phải còn ít nhất một người đang hoạt động có quyền quản lý phân quyền.");

    private ValidationProblemDetails InvalidRoleDataProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Role data is invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = "invalid_role_data";
        return problem;
    }

    private ProblemDetails Problem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{status}",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        return problem;
    }
}
