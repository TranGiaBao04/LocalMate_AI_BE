using System.Text.Json.Serialization;
using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.DTOs.Plans;

public abstract record AdminPlanTermsRequest
{
    public string? Name { get; init; }
    public decimal? Price { get; init; }
    public int? DurationDays { get; init; }
    public int? GenerateLimit { get; init; }
    public int? SavedTripLimit { get; init; }
    public IReadOnlyList<Guid>? FeatureIds { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAdminPlanRequest : AdminPlanTermsRequest
{
    public string? Code { get; init; }
    public int? EntitlementPriority { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAdminPlanRequest : AdminPlanTermsRequest;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminPlanStatusRequest
{
    [JsonRequired]
    public bool IsActive { get; init; }
}

public sealed record AdminPlanQuery : PagedQuery
{
    public static readonly IReadOnlyCollection<string> SortFields =
        ["code", "name", "entitlementPriority", "isActive", "isSystem", "createdAt"];
    public bool? IsActive { get; init; }
    public bool? IsSystem { get; init; }
}

public sealed record AdminPlanVersionsQuery : PagedQuery;

public sealed record AdminPlanFeatureResponse(Guid Id, string Code, string Name, string? Description);

public sealed record AdminPlanVersionResponse(
    Guid Id, int VersionNumber, decimal Price, int? DurationDays, int? GenerateLimit, int? SavedTripLimit,
    string Origin, DateTime? PublishedAt, DateTime CreatedAt,
    IReadOnlyList<AdminPlanFeatureResponse> Features, bool IsCurrent);

public sealed record AdminPlanResponse(
    Guid Id, string Code, string Name, int EntitlementPriority, bool IsSystem, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt, AdminPlanVersionResponse? CurrentVersion, int ActiveSubscriberCount);

public sealed record PlanVersionTerms(decimal Price, int? DurationDays, int? GenerateLimit,
    int? SavedTripLimit, IReadOnlyList<Guid> FeatureIds);

public enum AdminPlanResultStatus
{
    Success,
    ValidationFailed,
    NotFound,
    CodeExists,
    PriorityExists,
    SystemPlanLocked,
    PlanInUse,
    FreeCannotDeactivate,
    InvalidCurrentVersion
}

public sealed record AdminPlanResult(AdminPlanResultStatus Status, AdminPlanResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed record AdminPlanQueryResult<T>(AdminPlanResultStatus Status, PagedResult<T>? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
