namespace LocalMateAI.Application.DTOs.Roles;

public enum AdminRoleResultStatus
{
    Success,
    ValidationFailed,
    NotFound,
    NameExists,
    SystemRoleLocked,
    RoleInUse,
    LastRoleManager,
    CannotRemoveOwnRoleManager
}

public sealed record AdminRoleResult(
    AdminRoleResultStatus Status,
    AdminRoleResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static AdminRoleResult Succeeded(AdminRoleResponse? response = null) =>
        new(AdminRoleResultStatus.Success, response);

    public static AdminRoleResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        new(AdminRoleResultStatus.ValidationFailed, ValidationErrors: errors);

    public static AdminRoleResult Missing() => new(AdminRoleResultStatus.NotFound);

    public static AdminRoleResult NameTaken() => new(AdminRoleResultStatus.NameExists);

    public static AdminRoleResult SystemLocked() => new(AdminRoleResultStatus.SystemRoleLocked);

    public static AdminRoleResult InUse() => new(AdminRoleResultStatus.RoleInUse);

    public static AdminRoleResult LastManager() => new(AdminRoleResultStatus.LastRoleManager);

    public static AdminRoleResult OwnManagerRemoval() => new(AdminRoleResultStatus.CannotRemoveOwnRoleManager);
}

public enum AssignUserRoleResultStatus
{
    Success,
    InvalidRole,
    UserNotFound,
    CannotChangeOwnRole,
    LastRoleManager
}

public sealed record AssignUserRoleResult(
    AssignUserRoleResultStatus Status,
    UserRoleAssignmentResponse? Response = null)
{
    public static AssignUserRoleResult Succeeded(UserRoleAssignmentResponse response) =>
        new(AssignUserRoleResultStatus.Success, response);

    public static AssignUserRoleResult InvalidRole() => new(AssignUserRoleResultStatus.InvalidRole);

    public static AssignUserRoleResult UserMissing() => new(AssignUserRoleResultStatus.UserNotFound);

    public static AssignUserRoleResult OwnRoleChange() => new(AssignUserRoleResultStatus.CannotChangeOwnRole);

    public static AssignUserRoleResult LastManager() => new(AssignUserRoleResultStatus.LastRoleManager);
}
