using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Plans;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class AdminPlanServiceTests
{
    internal static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid Metro = PlanFeatureBaseline.MetroGoogleMapsId;
    internal static AdminPlanService Service(IAdminPlanRepository repo) => new(repo, new AdminPlanQueryValidator(),
        new AdminPlanVersionsQueryValidator(), new PlanVersionFoundationPostgresTests.Clock(Now));
    internal static CreateAdminPlanRequest Create(string code = "EXPLORER", int priority = 300) => new()
    { Code = code, Name = "Explorer", EntitlementPriority = priority, Price = 49000, DurationDays = 10,
        GenerateLimit = 2, SavedTripLimit = 4, AiDailyCallLimit = 15, AiExplainCallsPerTripLimit = 3, FeatureIds = [Metro] };
    internal static UpdateAdminPlanRequest Update() => new()
    { Name = "Explorer", Price = 49000, DurationDays = 10, GenerateLimit = 2, SavedTripLimit = 4,
        AiDailyCallLimit = 15, AiExplainCallsPerTripLimit = 3, FeatureIds = [Metro] };

    [Fact]
    public async Task Create_ServerOwnsIdentityVersionAndInactiveState()
    {
        var repo = new MemoryPlans();
        var result = await Service(repo).CreateAsync(Create());
        Assert.Equal(AdminPlanResultStatus.Success, result.Status);
        var plan = result.Response!;
        Assert.NotEqual(Guid.Empty, plan.Id);
        Assert.False(plan.IsSystem);
        Assert.False(plan.IsActive);
        Assert.Equal(1, plan.CurrentVersion!.VersionNumber);
        Assert.Equal("Published", plan.CurrentVersion.Origin);
        Assert.Equal(Now, plan.CurrentVersion.PublishedAt);
        Assert.Equal(Metro, Assert.Single(plan.CurrentVersion.Features).Id);
        Assert.Equal(1, repo.Publications);
    }

    [Theory]
    [InlineData("lower", 300)]
    [InlineData("BAD-CODE", 300)]
    [InlineData("1CODE", 300)]
    [InlineData(" CODE", 300)]
    [InlineData("CODE", -1)]
    public async Task InvalidIdentity_IsRejected(string code, int priority)
    {
        var repo = new MemoryPlans();
        Assert.Equal(AdminPlanResultStatus.ValidationFailed, (await Service(repo).CreateAsync(Create(code, priority))).Status);
        Assert.Empty(repo.Plans);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("longName")]
    [InlineData("missingPrice")]
    [InlineData("zeroPrice")]
    [InlineData("fractionalPrice")]
    [InlineData("largePrice")]
    [InlineData("missingDuration")]
    [InlineData("zeroDuration")]
    [InlineData("negativeGenerate")]
    [InlineData("negativeSaved")]
    [InlineData("duplicateFeatures")]
    [InlineData("unknownFeature")]
    [InlineData("emptyFeatureId")]
    public async Task InvalidTerms_AreRejectedWithoutPublication(string invalid)
    {
        var repo = new MemoryPlans();
        var request = invalid switch
        {
            "name" => Create() with { Name = " " },
            "longName" => Create() with { Name = new string('x', 201) },
            "missingPrice" => Create() with { Price = null },
            "zeroPrice" => Create() with { Price = 0 },
            "fractionalPrice" => Create() with { Price = 19000.5m },
            "largePrice" => Create() with { Price = 1_000_000_000_000m },
            "missingDuration" => Create() with { DurationDays = null },
            "zeroDuration" => Create() with { DurationDays = 0 },
            "negativeGenerate" => Create() with { GenerateLimit = -1 },
            "negativeSaved" => Create() with { SavedTripLimit = -1 },
            "duplicateFeatures" => Create() with { FeatureIds = [Metro, Metro] },
            "unknownFeature" => Create() with { FeatureIds = [Guid.NewGuid()] },
            _ => Create() with { FeatureIds = [Guid.Empty] }
        };
        var result = await Service(repo).CreateAsync(request);
        Assert.Equal(AdminPlanResultStatus.ValidationFailed, result.Status);
        Assert.NotEmpty(result.ValidationErrors!);
        Assert.Equal(0, repo.Publications);
    }

    [Fact]
    public async Task NameOnlyAndIdenticalUpdates_DoNotPublishOrWriteUnnecessarily()
    {
        var repo = new MemoryPlans();
        var service = Service(repo);
        var first = (await service.CreateAsync(Create())).Response!;
        await service.UpdateAsync(first.Id, Update());
        Assert.Equal(0, repo.Saves);
        var changed = await service.UpdateAsync(first.Id, Update() with { Name = "Renamed" });
        Assert.Equal("Renamed", changed.Response!.Name);
        Assert.Equal(first.CurrentVersion!.Id, changed.Response.CurrentVersion!.Id);
        Assert.Equal(1, repo.Publications);
        Assert.Equal(1, repo.Saves);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("duration")]
    [InlineData("generate")]
    [InlineData("saved")]
    [InlineData("features")]
    [InlineData("featureOrder")]
    public async Task CommercialChanges_PublishExactlyOneNextVersion(string change)
    {
        var repo = new MemoryPlans();
        var second = Guid.NewGuid();
        repo.Features.Add(second, new(second, "TEST_ONLY", "Test feature", null));
        var initial = Create() with { FeatureIds = [Metro, second] };
        var service = Service(repo);
        var first = (await service.CreateAsync(initial)).Response!;
        var same = Update() with { FeatureIds = initial.FeatureIds };
        var edited = change switch
        {
            "price" => same with { Price = 59000 }, "duration" => same with { DurationDays = 20 },
            "generate" => same with { GenerateLimit = null }, "saved" => same with { SavedTripLimit = 0 },
            "features" => same with { FeatureIds = [] }, _ => same with { FeatureIds = [second, Metro] }
        };
        var result = await service.UpdateAsync(first.Id, edited);
        Assert.Equal(2, result.Response!.CurrentVersion!.VersionNumber);
        Assert.Equal(2, repo.Publications);
        Assert.Equal(49000, repo.Versions[first.CurrentVersion!.Id].Price);
        Assert.Equal(new[] { Metro, second }, repo.Selections[first.CurrentVersion.Id]);
        await service.UpdateAsync(first.Id, edited);
        Assert.Equal(2, repo.Publications);
    }

    [Fact]
    public async Task ZeroQuotasAndZeroFeatures_AreValid()
    {
        var repo = new MemoryPlans();
        var result = await Service(repo).CreateAsync(Create() with { GenerateLimit = 0, SavedTripLimit = 0, FeatureIds = [] });
        Assert.Equal(AdminPlanResultStatus.Success, result.Status);
        Assert.Empty(result.Response!.CurrentVersion!.Features);
    }

    [Fact]
    public async Task FreeCanPublishQuotaVersion_ButCannotDeactivateOrDelete()
    {
        var repo = new MemoryPlans();
        var free = repo.AddFree();
        var service = Service(repo);
        var result = await service.UpdateAsync(free.Id, new() { Name = "Free", Price = 0, DurationDays = null,
            GenerateLimit = 3, SavedTripLimit = 1, AiDailyCallLimit = 3, AiExplainCallsPerTripLimit = 1, FeatureIds = [] });
        Assert.Equal(2, result.Response!.CurrentVersion!.VersionNumber);
        Assert.Equal(AdminPlanResultStatus.FreeCannotDeactivate, (await service.SetStatusAsync(free.Id, false)).Status);
        Assert.Equal(AdminPlanResultStatus.SystemPlanLocked, (await service.DeleteAsync(free.Id)).Status);
        Assert.True(free.IsActive);
        Assert.Equal(1, repo.Versions[SubscriptionBaseline.VersionId(PlanCode.Free)].GenerateLimit);
        Assert.Equal(AdminPlanResultStatus.ValidationFailed,
            (await service.UpdateAsync(free.Id, Update() with { Price = 1, DurationDays = null })).Status);
    }

    [Fact]
    public async Task Lifecycle_SameStateNoWrite_ActivationRequiresValidCurrentVersion()
    {
        var repo = new MemoryPlans();
        var service = Service(repo);
        var created = (await service.CreateAsync(Create())).Response!;
        await service.SetStatusAsync(created.Id, false);
        Assert.Equal(0, repo.Saves);
        await service.SetStatusAsync(created.Id, true);
        await service.SetStatusAsync(created.Id, true);
        await service.SetStatusAsync(created.Id, false);
        Assert.Equal(2, repo.Saves);
        Assert.Equal(1, repo.Publications);
        var unpublished = new SubscriptionPlan { Code = "UNPUBLISHED", EntitlementPriority = 400, IsActive = false };
        repo.Plans.Add(unpublished.Id, unpublished);
        Assert.Equal(AdminPlanResultStatus.InvalidCurrentVersion,
            (await service.SetStatusAsync(unpublished.Id, true)).Status);
    }

    [Fact]
    public async Task Delete_OnlyUnreferencedUnpublishedCustomIdentity()
    {
        var repo = new MemoryPlans();
        var service = Service(repo);
        var published = (await service.CreateAsync(Create())).Response!;
        Assert.Equal(AdminPlanResultStatus.PlanInUse, (await service.DeleteAsync(published.Id)).Status);
        var unpublished = new SubscriptionPlan { Code = "UNPUBLISHED", EntitlementPriority = 400, IsActive = false };
        repo.Plans.Add(unpublished.Id, unpublished);
        Assert.Equal(AdminPlanResultStatus.Success, (await service.DeleteAsync(unpublished.Id)).Status);
        Assert.False(repo.Plans.ContainsKey(unpublished.Id));
    }

    [Fact]
    public async Task MissingPlan_AllOperationsReturnNotFound()
    {
        var service = Service(new MemoryPlans());
        var id = Guid.NewGuid();
        Assert.Null(await service.GetPlanAsync(id));
        Assert.Equal(AdminPlanResultStatus.NotFound, (await service.UpdateAsync(id, Update())).Status);
        Assert.Equal(AdminPlanResultStatus.NotFound, (await service.SetStatusAsync(id, true)).Status);
        Assert.Equal(AdminPlanResultStatus.NotFound, (await service.DeleteAsync(id)).Status);
        Assert.Equal(AdminPlanResultStatus.NotFound, (await service.GetVersionsAsync(id, new())).Status);
    }

    [Theory]
    [InlineData(0, 20, null, null)]
    [InlineData(1, 0, null, null)]
    [InlineData(1, 101, null, null)]
    [InlineData(10001, 20, null, null)]
    [InlineData(1, 20, "price; DROP TABLE", null)]
    [InlineData(1, 20, "name", "sideways")]
    public async Task InvalidPagingAndSort_AreRejected(int page, int size, string? sort, string? direction)
    {
        var result = await Service(new MemoryPlans()).GetPlansAsync(new()
        { Page = page, PageSize = size, SortBy = sort, SortDirection = direction });
        Assert.Equal(AdminPlanResultStatus.ValidationFailed, result.Status);
    }

    internal sealed class MemoryPlans : IAdminPlanRepository
    {
        internal readonly Dictionary<Guid, SubscriptionPlan> Plans = [];
        internal readonly Dictionary<Guid, SubscriptionPlanVersion> Versions = [];
        internal readonly Dictionary<Guid, Guid[]> Selections = [];
        internal readonly Dictionary<Guid, AdminPlanFeatureResponse> Features = new()
        { [Metro] = new(Metro, "METRO_GOOGLE_MAPS", "Metro & Maps", null) };
        internal int Publications, Saves;
        internal SubscriptionPlan AddFree()
        {
            var p = SubscriptionBaseline.Plans().Single(p => p.Code == PlanIdentity.Free);
            var v = SubscriptionBaseline.Versions().Single(v => v.PlanId == p.Id);
            Plans.Add(p.Id, p); Versions.Add(v.Id, v); Selections.Add(v.Id, [Metro]); return p;
        }
        private AdminPlanVersionResponse Version(SubscriptionPlanVersion v, Guid? current) => new(v.Id, v.VersionNumber,
            v.Price, v.DurationDays, v.GenerateLimit, v.SavedTripLimit, v.Origin.ToString(), v.PublishedAt, v.CreatedAt,
            Selections[v.Id].Select(id => Features[id]).ToArray(), v.Id == current);
        private AdminPlanResponse Response(SubscriptionPlan p) => new(p.Id, p.Code, p.Name, p.EntitlementPriority,
            p.IsSystem, p.IsActive, p.CreatedAt, p.UpdatedAt, p.CurrentVersionId is { } id ? Version(Versions[id], id) : null, 0);
        public Task<AdminPlanResponse?> GetPlanAsync(Guid id, DateTime now, CancellationToken ct = default) =>
            Task.FromResult(Plans.TryGetValue(id, out var p) ? Response(p) : null);
        public Task<PagedResult<AdminPlanResponse>> GetPlansAsync(AdminPlanQuery q, DateTime now, CancellationToken ct = default) =>
            Task.FromResult(PagedResult<AdminPlanResponse>.Create(Plans.Values.Select(Response).ToArray(), q.Page, q.PageSize, Plans.Count));
        public Task<PagedResult<AdminPlanVersionResponse>> GetVersionsAsync(Guid id, AdminPlanVersionsQuery q, CancellationToken ct = default)
        {
            var items = Versions.Values.Where(v => v.PlanId == id).OrderByDescending(v => v.VersionNumber)
                .Select(v => Version(v, Plans[id].CurrentVersionId)).ToArray();
            return Task.FromResult(PagedResult<AdminPlanVersionResponse>.Create(items, q.Page, q.PageSize, items.Length));
        }
        public Task<IReadOnlyList<AdminPlanFeatureResponse>> GetFeaturesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdminPlanFeatureResponse>>(Features.Values.OrderBy(f => f.Code).ToArray());
        public Task<bool> FeaturesExistAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(ids.All(Features.ContainsKey));
        public Task<SubscriptionPlanVersion?> GetVersionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Versions.GetValueOrDefault(id));
        public Task<IReadOnlyList<Guid>> GetVersionFeatureIdsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>(Selections[id]);
        public async Task<AdminPlanResultStatus> CreateAsync(SubscriptionPlan plan, PlanVersionTerms terms, DateTime now, CancellationToken ct = default)
        {
            if (Plans.Values.Any(p => p.Code == plan.Code)) return AdminPlanResultStatus.CodeExists;
            if (Plans.Values.Any(p => p.EntitlementPriority == plan.EntitlementPriority)) return AdminPlanResultStatus.PriorityExists;
            Plans.Add(plan.Id, plan); await PublishNextVersionAsync(plan, terms, now, ct); return AdminPlanResultStatus.Success;
        }
        public Task<T> ExecuteLockedAsync<T>(Guid id, Func<SubscriptionPlan?, CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(Plans.GetValueOrDefault(id), ct);
        public Task PublishNextVersionAsync(SubscriptionPlan plan, PlanVersionTerms terms, DateTime now, CancellationToken ct = default)
        {
            var v = new SubscriptionPlanVersion { PlanId = plan.Id,
                VersionNumber = Versions.Values.Count(v => v.PlanId == plan.Id) + 1, Price = terms.Price,
                DurationDays = terms.DurationDays, GenerateLimit = terms.GenerateLimit, SavedTripLimit = terms.SavedTripLimit,
                AiDailyCallLimit = terms.AiDailyCallLimit, AiExplainCallsPerTripLimit = terms.AiExplainCallsPerTripLimit,
                Origin = PlanVersionOrigin.Published, PublishedAt = now };
            Versions.Add(v.Id, v); Selections.Add(v.Id, terms.FeatureIds.ToArray()); plan.CurrentVersionId = v.Id;
            Publications++; return Task.CompletedTask;
        }
        public Task SaveAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
        public Task<bool> IsReferencedAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Versions.Values.Any(v => v.PlanId == id));
        public Task<bool> TryDeleteAsync(SubscriptionPlan plan, CancellationToken ct = default) => Task.FromResult(Plans.Remove(plan.Id));
    }
}
