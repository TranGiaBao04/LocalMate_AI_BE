using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class AdminPlanService(
    IAdminPlanRepository repository,
    IValidator<AdminPlanQuery> queryValidator,
    IValidator<AdminPlanVersionsQuery> historyValidator,
    TimeProvider timeProvider) : IAdminPlanService
{
    private static readonly Regex CodePattern = new("^[A-Z][A-Z0-9_]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private const decimal MaxPrice = 999_999_999_999m;
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<AdminPlanQueryResult<AdminPlanResponse>> GetPlansAsync(AdminPlanQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = await queryValidator.ValidateAsync(query, cancellationToken);
        return validation.IsValid
            ? new(AdminPlanResultStatus.Success, await repository.GetPlansAsync(query, Now, cancellationToken))
            : new(AdminPlanResultStatus.ValidationFailed, ValidationErrors: Errors(validation));
    }

    public Task<AdminPlanResponse?> GetPlanAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.GetPlanAsync(id, Now, cancellationToken);
    public Task<IReadOnlyList<AdminPlanFeatureResponse>> GetFeaturesAsync(CancellationToken cancellationToken = default) =>
        repository.GetFeaturesAsync(cancellationToken);

    public async Task<AdminPlanQueryResult<AdminPlanVersionResponse>> GetVersionsAsync(Guid id,
        AdminPlanVersionsQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await historyValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return new(AdminPlanResultStatus.ValidationFailed, ValidationErrors: Errors(validation));
        if (await GetPlanAsync(id, cancellationToken) is null) return new(AdminPlanResultStatus.NotFound);
        return new(AdminPlanResultStatus.Success, await repository.GetVersionsAsync(id, query, cancellationToken));
    }

    public async Task<AdminPlanResult> CreateAsync(CreateAdminPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = Validate(request, isFree: false);
        if (request.Code is null || !CodePattern.IsMatch(request.Code))
            errors["code"] = ["Use a canonical uppercase code, maximum 64 characters."];
        if (request.EntitlementPriority is null or < 0)
            errors["entitlementPriority"] = ["A non-negative unique priority is required."];
        await ValidateFeaturesAsync(request, errors, cancellationToken);
        if (errors.Count > 0) return Invalid(errors);
        var plan = new SubscriptionPlan
        {
            Code = request.Code!, Name = request.Name!.Trim(), EntitlementPriority = request.EntitlementPriority!.Value,
            IsSystem = false, IsActive = false
        };
        var status = await repository.CreateAsync(plan, Terms(request), Now, cancellationToken);
        return status == AdminPlanResultStatus.Success
            ? new(status, await GetPlanAsync(plan.Id, cancellationToken)) : new(status);
    }

    public Task<AdminPlanResult> UpdateAsync(Guid id, UpdateAdminPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return repository.ExecuteLockedAsync(id, async (plan, ct) =>
        {
            if (plan is null) return new AdminPlanResult(AdminPlanResultStatus.NotFound);
            var errors = Validate(request, plan.Code == PlanIdentity.Free);
            await ValidateFeaturesAsync(request, errors, ct);
            if (errors.Count > 0) return Invalid(errors);
            var terms = Terms(request);
            var current = plan.CurrentVersionId is { } versionId ? await repository.GetVersionAsync(versionId, ct) : null;
            var featureIds = current is null ? [] : await repository.GetVersionFeatureIdsAsync(current.Id, ct);
            var changed = current is null || current.Price != terms.Price || current.DurationDays != terms.DurationDays
                || current.GenerateLimit != terms.GenerateLimit || current.SavedTripLimit != terms.SavedTripLimit
                || !featureIds.SequenceEqual(terms.FeatureIds);
            var name = request.Name!.Trim();
            var renamed = plan.Name != name;
            if (renamed) plan.Name = name;
            if (changed) await repository.PublishNextVersionAsync(plan, terms, Now, ct);
            else if (renamed) await repository.SaveAsync(ct);
            return new AdminPlanResult(AdminPlanResultStatus.Success, await GetPlanAsync(id, ct));
        }, cancellationToken);
    }

    public Task<AdminPlanResult> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default) =>
        repository.ExecuteLockedAsync(id, async (plan, ct) =>
        {
            if (plan is null) return new AdminPlanResult(AdminPlanResultStatus.NotFound);
            if (!isActive && plan.Code == PlanIdentity.Free)
                return new AdminPlanResult(AdminPlanResultStatus.FreeCannotDeactivate);
            if (plan.IsActive != isActive)
            {
                if (isActive)
                {
                    var version = plan.CurrentVersionId is { } versionId
                        ? await repository.GetVersionAsync(versionId, ct) : null;
                    if (version is null || version.PlanId != plan.Id || !ValidVersion(version, plan.Code == PlanIdentity.Free))
                        return new AdminPlanResult(AdminPlanResultStatus.InvalidCurrentVersion);
                }
                plan.IsActive = isActive;
                await repository.SaveAsync(ct);
            }
            return new AdminPlanResult(AdminPlanResultStatus.Success, await GetPlanAsync(id, ct));
        }, cancellationToken);

    public Task<AdminPlanResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.ExecuteLockedAsync(id, async (plan, ct) =>
        {
            if (plan is null) return new AdminPlanResult(AdminPlanResultStatus.NotFound);
            if (plan.IsSystem || plan.Code == PlanIdentity.Free)
                return new AdminPlanResult(AdminPlanResultStatus.SystemPlanLocked);
            if (plan.CurrentVersionId is not null || await repository.IsReferencedAsync(id, ct))
                return new AdminPlanResult(AdminPlanResultStatus.PlanInUse);
            return new AdminPlanResult(await repository.TryDeleteAsync(plan, ct)
                ? AdminPlanResultStatus.Success : AdminPlanResultStatus.PlanInUse);
        }, cancellationToken);

    private async Task ValidateFeaturesAsync(AdminPlanTermsRequest request, Dictionary<string, string[]> errors,
        CancellationToken ct)
    {
        var ids = request.FeatureIds ?? [];
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            errors["featureIds"] = ["Feature IDs must be distinct, non-empty IDs."];
        else if (!await repository.FeaturesExistAsync(ids, ct))
            errors["featureIds"] = ["One or more features do not exist."];
    }

    private static Dictionary<string, string[]> Validate(AdminPlanTermsRequest request, bool isFree)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            errors["name"] = ["Name is required and must not exceed 200 characters."];
        if (request.Price is null || request.Price < 0 || request.Price > MaxPrice
            || decimal.Truncate(request.Price.Value) != request.Price.Value
            || (isFree ? request.Price != 0 : request.Price <= 0))
            errors["price"] = [isFree ? "Free must cost 0 VND." : "Paid price must be positive whole VND within 12 digits."];
        if (isFree ? request.DurationDays is not null : request.DurationDays is null or <= 0)
            errors["durationDays"] = [isFree ? "Free has no duration." : "Paid duration must be positive."];
        if (request.GenerateLimit < 0) errors["generateLimit"] = ["Generate limit must be null or non-negative."];
        if (request.SavedTripLimit < 0) errors["savedTripLimit"] = ["Saved trip limit must be null or non-negative."];
        return errors;
    }

    private static bool ValidVersion(SubscriptionPlanVersion version, bool isFree) =>
        Validate(new UpdateAdminPlanRequest { Name = "Stored contract", Price = version.Price,
            DurationDays = version.DurationDays, GenerateLimit = version.GenerateLimit, SavedTripLimit = version.SavedTripLimit },
            isFree).Count == 0 && (version.Origin != PlanVersionOrigin.Published || version.PublishedAt is not null);
    private static PlanVersionTerms Terms(AdminPlanTermsRequest request) =>
        new(request.Price!.Value, request.DurationDays, request.GenerateLimit, request.SavedTripLimit,
            (request.FeatureIds ?? []).ToArray());
    private static AdminPlanResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(AdminPlanResultStatus.ValidationFailed, ValidationErrors: errors);
    private static IReadOnlyDictionary<string, string[]> Errors(ValidationResult result) =>
        result.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
}
