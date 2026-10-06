using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminPlanRepository(AppDbContext context) : IAdminPlanRepository
{
    private static readonly SortMap<SubscriptionPlan> Sort = new SortMap<SubscriptionPlan>(
        "entitlementPriority", false, p => p.Id)
        .Add("code", p => p.Code).Add("name", p => p.Name)
        .Add("entitlementPriority", p => p.EntitlementPriority).Add("isActive", p => p.IsActive)
        .Add("isSystem", p => p.IsSystem).Add("createdAt", p => p.CreatedAt);

    public async Task<PagedResult<AdminPlanResponse>> GetPlansAsync(AdminPlanQuery query, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var plans = context.SubscriptionPlans.AsNoTracking();
        if (query.NormalizedSearch is { } search)
        {
            var upper = search.ToUpperInvariant();
            plans = plans.Where(p => p.Code.ToUpper().Contains(upper) || p.Name.ToUpper().Contains(upper));
        }
        if (query.IsActive is { } active) plans = plans.Where(p => p.IsActive == active);
        if (query.IsSystem is { } system) plans = plans.Where(p => p.IsSystem == system);
        var page = await Rows(plans.ApplySort(query, Sort), nowUtc).ToPagedResultAsync(query, cancellationToken);
        return PagedResult<AdminPlanResponse>.Create(await ResponsesAsync(page.Items, cancellationToken),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<AdminPlanResponse?> GetPlanAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var row = await Rows(context.SubscriptionPlans.AsNoTracking().Where(p => p.Id == id), nowUtc)
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : (await ResponsesAsync([row], cancellationToken))[0];
    }

    private IQueryable<PlanRow> Rows(IQueryable<SubscriptionPlan> plans, DateTime now) => plans.Select(p => new PlanRow(
        p.Id, p.Code, p.Name, p.EntitlementPriority, p.IsSystem, p.IsActive, p.CreatedAt, p.UpdatedAt,
        p.CurrentVersionId, context.SubscriptionPeriods.Where(s => s.PlanId == p.Id && s.StartsAt <= now && now < s.EndsAt
            && (s.TerminatedAt == null || now < s.TerminatedAt))
            .Select(s => s.UserId).Distinct().Count()));

    private async Task<IReadOnlyList<AdminPlanResponse>> ResponsesAsync(IReadOnlyList<PlanRow> rows, CancellationToken ct)
    {
        var ids = rows.Where(p => p.CurrentVersionId.HasValue).Select(p => p.CurrentVersionId!.Value).ToArray();
        var versions = await context.SubscriptionPlanVersions.AsNoTracking().Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        var features = await VersionFeaturesAsync(ids, ct);
        var current = versions.ToDictionary(v => v.Id, v => VersionResponse(v, features, true));
        return rows.Select(p => new AdminPlanResponse(p.Id, p.Code, p.Name, p.Priority, p.IsSystem, p.IsActive,
            p.CreatedAt, p.UpdatedAt, p.CurrentVersionId is { } id && current.TryGetValue(id, out var v) ? v : null,
            p.SubscriberCount)).ToArray();
    }

    public async Task<PagedResult<AdminPlanVersionResponse>> GetVersionsAsync(Guid id, AdminPlanVersionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await context.SubscriptionPlanVersions.AsNoTracking().Where(v => v.PlanId == id)
            .OrderByDescending(v => v.VersionNumber).ThenBy(v => v.Id)
            .Select(v => new HistoryRow(v, context.SubscriptionPlans.Any(p => p.Id == id && p.CurrentVersionId == v.Id)))
            .ToPagedResultAsync(query, cancellationToken);
        var features = await VersionFeaturesAsync(page.Items.Select(v => v.Version.Id).ToArray(), cancellationToken);
        return PagedResult<AdminPlanVersionResponse>.Create(
            page.Items.Select(v => VersionResponse(v.Version, features, v.IsCurrent)).ToArray(),
            page.Page, page.PageSize, page.TotalCount);
    }

    private async Task<Dictionary<Guid, AdminPlanFeatureResponse[]>> VersionFeaturesAsync(Guid[] ids, CancellationToken ct)
    {
        var rows = await (from selected in context.SubscriptionPlanVersionFeatures.AsNoTracking()
            join feature in context.PlanFeatures.AsNoTracking() on selected.FeatureId equals feature.Id
            where ids.Contains(selected.PlanVersionId)
            orderby selected.SortOrder, feature.Code
            select new { selected.PlanVersionId, Feature = new AdminPlanFeatureResponse(feature.Id, feature.Code,
                feature.Name, feature.Description) }).ToListAsync(ct);
        return rows.GroupBy(f => f.PlanVersionId).ToDictionary(g => g.Key, g => g.Select(f => f.Feature).ToArray());
    }

    private static AdminPlanVersionResponse VersionResponse(SubscriptionPlanVersion v,
        Dictionary<Guid, AdminPlanFeatureResponse[]> features, bool current) =>
        new(v.Id, v.VersionNumber, v.Price, v.DurationDays, v.GenerateLimit, v.SavedTripLimit, v.Origin.ToString(),
            v.PublishedAt, v.CreatedAt, features.GetValueOrDefault(v.Id, []), current)
        {
            AiDailyCallLimit = v.AiDailyCallLimit,
            AiExplainCallsPerTripLimit = v.AiExplainCallsPerTripLimit
        };

    public async Task<IReadOnlyList<AdminPlanFeatureResponse>> GetFeaturesAsync(CancellationToken cancellationToken = default) =>
        await context.PlanFeatures.AsNoTracking().OrderBy(f => f.Code)
            .Select(f => new AdminPlanFeatureResponse(f.Id, f.Code, f.Name, f.Description)).ToListAsync(cancellationToken);
    public async Task<bool> FeaturesExistAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        await context.PlanFeatures.CountAsync(f => ids.Contains(f.Id), cancellationToken) == ids.Count;
    public Task<SubscriptionPlanVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.SubscriptionPlanVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
    public async Task<IReadOnlyList<Guid>> GetVersionFeatureIdsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.SubscriptionPlanVersionFeatures.AsNoTracking().Where(f => f.PlanVersionId == id)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.FeatureId).Select(f => f.FeatureId).ToArrayAsync(cancellationToken);

    public async Task<AdminPlanResultStatus> CreateAsync(SubscriptionPlan plan, PlanVersionTerms terms, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            context.SubscriptionPlans.Add(plan);
            await context.SaveChangesAsync(cancellationToken);
            await LockAsync(plan.Id, cancellationToken);
            await PublishNextVersionAsync(plan, terms, nowUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminPlanResultStatus.Success;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName is "IX_SubscriptionPlans_Code" or "IX_SubscriptionPlans_EntitlementPriority")
        {
            await transaction.RollbackAsync(cancellationToken);
            context.Entry(plan).State = EntityState.Detached;
            return ((PostgresException)e.InnerException!).ConstraintName == "IX_SubscriptionPlans_Code"
                ? AdminPlanResultStatus.CodeExists : AdminPlanResultStatus.PriorityExists;
        }
    }

    public async Task<T> ExecuteLockedAsync<T>(Guid id,
        Func<SubscriptionPlan?, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(id, cancellationToken);
        var plan = await context.SubscriptionPlans.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is not null) await context.Entry(plan).ReloadAsync(cancellationToken);
        var result = await operation(plan, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private Task LockAsync(Guid id, CancellationToken ct) => context.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT 1 FROM \"SubscriptionPlans\" WHERE \"Id\" = {id} FOR UPDATE", ct);

    // Version, ordered associations and current pointer commit together, under the identity lock.
    public async Task PublishNextVersionAsync(SubscriptionPlan plan, PlanVersionTerms terms, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Plan publication requires a transaction.");
        await LockAsync(plan.Id, cancellationToken);
        var latest = await context.SubscriptionPlanVersions.Where(v => v.PlanId == plan.Id)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken) ?? 0;
        var version = new SubscriptionPlanVersion
        {
            PlanId = plan.Id, VersionNumber = checked(latest + 1), Price = terms.Price, DurationDays = terms.DurationDays,
            GenerateLimit = terms.GenerateLimit, SavedTripLimit = terms.SavedTripLimit,
            AiDailyCallLimit = terms.AiDailyCallLimit, AiExplainCallsPerTripLimit = terms.AiExplainCallsPerTripLimit,
            Origin = PlanVersionOrigin.Published, PublishedAt = nowUtc
        };
        context.SubscriptionPlanVersions.Add(version);
        context.SubscriptionPlanVersionFeatures.AddRange(terms.FeatureIds.Select((id, order) =>
            new SubscriptionPlanVersionFeature { PlanVersionId = version.Id, FeatureId = id, SortOrder = order }));
        await context.SaveChangesAsync(cancellationToken);
        plan.CurrentVersionId = version.Id;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default) => await context.SaveChangesAsync(cancellationToken);
    public async Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.SubscriptionPlanVersions.AnyAsync(v => v.PlanId == id, cancellationToken)
        || await context.PaymentOrders.AnyAsync(o => o.PlanId == id, cancellationToken)
        || await context.SubscriptionPeriods.AnyAsync(p => p.PlanId == id, cancellationToken);
    public async Task<bool> TryDeleteAsync(SubscriptionPlan plan, CancellationToken cancellationToken = default)
    {
        context.SubscriptionPlans.Remove(plan);
        try { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            context.Entry(plan).State = EntityState.Detached;
            return false;
        }
    }

    private sealed record PlanRow(Guid Id, string Code, string Name, int Priority, bool IsSystem, bool IsActive,
        DateTime CreatedAt, DateTime UpdatedAt, Guid? CurrentVersionId, int SubscriberCount);
    private sealed record HistoryRow(SubscriptionPlanVersion Version, bool IsCurrent);
}
