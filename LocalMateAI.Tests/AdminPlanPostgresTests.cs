using System.Net;
using System.Net.Http.Json;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LocalMateAI.Tests;

public sealed class AdminPlanPostgresTests
{
    private static readonly DateTime Now = AdminPlanServiceTests.Now;
    private static readonly Guid Metro = AdminPlanServiceTests.Metro;
    private static readonly Guid TripPlan = SubscriptionBaseline.PlanId(PlanCode.TripPass);
    private static AdminPlanService Service(AppDbContext c) => AdminPlanServiceTests.Service(new AdminPlanRepository(c));
    private static CreateAdminPlanRequest Create(string code = "EXPLORER", int priority = 300) => AdminPlanServiceTests.Create(code, priority);
    private static UpdateAdminPlanRequest Update() => AdminPlanServiceTests.Update();

    [Fact]
    public async Task AiFields_RealRepositoryListDetailHistoryAndCatalogExposeStoredVersions()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = Service(c);
        var list = (await service.GetPlansAsync(new())).Response!;
        foreach (var (code, daily, explain) in new[] { ("FREE", 3, 1), ("TRIP_PASS", 15, 3), ("MEMBERSHIP", 30, 3) })
        {
            var row = list.Items.Single(p => p.Code == code);
            var detail = (await service.GetPlanAsync(row.Id))!;
            var history = (await service.GetVersionsAsync(row.Id, new())).Response!.Items;
            Assert.Equal(daily, row.CurrentVersion!.AiDailyCallLimit);
            Assert.Equal(explain, row.CurrentVersion.AiExplainCallsPerTripLimit);
            Assert.Equal(daily, detail.CurrentVersion!.AiDailyCallLimit);
            Assert.Equal(explain, detail.CurrentVersion.AiExplainCallsPerTripLimit);
            Assert.Equal(daily, Assert.Single(history).AiDailyCallLimit);
            Assert.Equal(explain, Assert.Single(history).AiExplainCallsPerTripLimit);
        }
        var created = (await service.CreateAsync(Create())).Response!;
        var original = created.CurrentVersion!;
        var updated = (await service.UpdateAsync(created.Id, Update() with
            { AiDailyCallLimit = 22, AiExplainCallsPerTripLimit = 2 })).Response!;
        Assert.Equal(22, updated.CurrentVersion!.AiDailyCallLimit);
        Assert.Equal(2, updated.CurrentVersion.AiExplainCallsPerTripLimit);
        var old = (await service.GetVersionsAsync(created.Id, new())).Response!.Items.Single(v => v.Id == original.Id);
        Assert.Equal(15, old.AiDailyCallLimit);
        Assert.Equal(3, old.AiExplainCallsPerTripLimit);
        var consumer = new SubscriptionService(new UserRepository(c), new SubscriptionRepository(c),
            new UsageEventRepository(c), new TripRepository(c), new PlanVersionFoundationPostgresTests.Clock(Now),
            new AiUsageAdmissionRepository(c, new FakeSystemSettingProvider(), TimeProvider.System), new FakeSystemSettingProvider());
        Assert.Equal(new (int?, int?)[] { (3, 1), (15, 3), (30, 3) },
            (await consumer.GetPlansAsync()).Select(p => (p.AiDailyCallLimit, p.AiExplainCallsPerTripLimit)));
    }

    [Fact]
    public async Task AiFields_HistoricalCustomNullsRemainNullInRealRepositoryResponses()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(
            targetMigration: "20261005084944_AddTripAiExplainedAt");
        var plan = new SubscriptionPlan { Code = "LEGACY_AI", Name = "Legacy AI", EntitlementPriority = 900 };
        await using (var historical = db.ContextBeforeUserLockedBy())
        {
            historical.SubscriptionPlans.Add(plan);
            await historical.SaveChangesAsync();
            var version = new SubscriptionPlanVersion
            {
                PlanId = plan.Id, VersionNumber = 1, Price = 19000, DurationDays = 7,
                Origin = PlanVersionOrigin.Published, PublishedAt = Now
            };
            historical.SubscriptionPlanVersions.Add(version);
            await historical.SaveChangesAsync();
            plan.CurrentVersionId = version.Id;
            await historical.SaveChangesAsync();
        }
        await using var c = db.Context();
        await c.Database.MigrateAsync();
        var service = Service(c);
        var list = (await service.GetPlansAsync(new())).Response!.Items.Single(p => p.Id == plan.Id);
        var detail = (await service.GetPlanAsync(plan.Id))!;
        var history = Assert.Single((await service.GetVersionsAsync(plan.Id, new())).Response!.Items);
        foreach (var response in new[] { list.CurrentVersion!, detail.CurrentVersion!, history })
        {
            Assert.Null(response.AiDailyCallLimit);
            Assert.Null(response.AiExplainCallsPerTripLimit);
        }
    }

    [Fact]
    public async Task CreateCustomV1_AtomicServerFieldsFeatures_AndNoSchemaChange()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var result = await Service(c).CreateAsync(Create());
        Assert.Equal(AdminPlanResultStatus.Success, result.Status);
        var plan = result.Response!;
        Assert.Equal("EXPLORER", plan.Code);
        Assert.False(plan.IsActive);
        Assert.False(plan.IsSystem);
        Assert.Equal(1, plan.CurrentVersion!.VersionNumber);
        Assert.Equal("Published", plan.CurrentVersion.Origin);
        Assert.Equal(Now, plan.CurrentVersion.PublishedAt);
        Assert.Equal(Metro, Assert.Single(plan.CurrentVersion.Features).Id);
        Assert.Equal(4, await c.SubscriptionPlans.CountAsync());
        Assert.Equal(4, await c.SubscriptionPlanVersions.CountAsync());
        Assert.Equal(4, await c.SubscriptionPlanVersionFeatures.CountAsync());
        Assert.False(c.Database.HasPendingModelChanges());
        Assert.Contains("20260930143244_AddVersionedPlanFeatures", await c.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await c.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task UniqueCodePriority_StableConflicts_NoPartialRows()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = Service(c);
        Assert.Equal(AdminPlanResultStatus.Success, (await service.CreateAsync(Create())).Status);
        Assert.Equal(AdminPlanResultStatus.CodeExists, (await service.CreateAsync(Create(priority: 400))).Status);
        Assert.Equal(AdminPlanResultStatus.PriorityExists, (await service.CreateAsync(Create("OTHER"))).Status);
        Assert.Equal(4, await c.SubscriptionPlans.CountAsync());
        Assert.Equal(4, await c.SubscriptionPlanVersions.CountAsync());
    }

    [Fact]
    public async Task NoopAndNameOnly_KeepVersionAndUnchangedWriteTimestamp()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = Service(c);
        var original = (await service.CreateAsync(Create())).Response!;
        var same = (await service.UpdateAsync(original.Id, Update())).Response!;
        Assert.Equal(original.UpdatedAt, same.UpdatedAt);
        Assert.Equal(original.CurrentVersion!.Id, same.CurrentVersion!.Id);
        var renamed = (await service.UpdateAsync(original.Id, Update() with { Name = "Renamed" })).Response!;
        Assert.Equal("Renamed", renamed.Name);
        Assert.Equal(original.CurrentVersion.Id, renamed.CurrentVersion!.Id);
        Assert.Equal(1, await c.SubscriptionPlanVersions.CountAsync(v => v.PlanId == original.Id));
        var unchanged = (await service.SetStatusAsync(original.Id, false)).Response!;
        Assert.Equal(renamed.UpdatedAt, unchanged.UpdatedAt);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("quota")]
    [InlineData("features")]
    [InlineData("order")]
    public async Task VersionedTermsAndOrderedFeatures_ImmutableHistory(string change)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var testFeature = new PlanFeature { Code = "TEST_FIXTURE", Name = "Isolated fixture only" };
        c.PlanFeatures.Add(testFeature);
        await c.SaveChangesAsync();
        var service = Service(c);
        var first = (await service.CreateAsync(Create() with { FeatureIds = [Metro, testFeature.Id] })).Response!;
        var request = Update() with { FeatureIds = [Metro, testFeature.Id] };
        request = change switch
        {
            "price" => request with { Price = 69000 }, "quota" => request with { GenerateLimit = null, SavedTripLimit = 0 },
            "features" => request with { FeatureIds = [] }, _ => request with { FeatureIds = [testFeature.Id, Metro] }
        };
        var second = (await service.UpdateAsync(first.Id, request)).Response!;
        Assert.Equal(2, second.CurrentVersion!.VersionNumber);
        var history = (await service.GetVersionsAsync(first.Id, new())).Response!;
        Assert.Equal(new[] { 2, 1 }, history.Items.Select(v => v.VersionNumber));
        Assert.True(history.Items[0].IsCurrent);
        Assert.False(history.Items[1].IsCurrent);
        Assert.Equal(49000, history.Items[1].Price);
        Assert.Equal(2, history.Items[1].GenerateLimit);
        Assert.Equal(new[] { Metro, testFeature.Id }, history.Items[1].Features.Select(f => f.Id));
        Assert.Equal(request.FeatureIds!, history.Items[0].Features.Select(f => f.Id));
        var page = (await service.GetVersionsAsync(first.Id, new() { Page = 2, PageSize = 1 })).Response!;
        Assert.Equal(1, Assert.Single(page.Items).VersionNumber);
        Assert.Equal(2, page.TotalPages);
        await service.UpdateAsync(first.Id, request);
        Assert.Equal(2, await c.SubscriptionPlanVersions.CountAsync(v => v.PlanId == first.Id));
        var mutation = await Assert.ThrowsAsync<PostgresException>(() => c.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SubscriptionPlanVersions\" SET \"Price\"=1 WHERE \"Id\"={first.CurrentVersion!.Id}"));
        Assert.Equal("23514", mutation.SqlState);
        c.ChangeTracker.Clear();
        var v1Id = first.CurrentVersion!.Id;
        var v1 = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == v1Id);
        c.Entry(v1).Property(v => v.GenerateLimit).CurrentValue = 999;
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentDifferentUpdates_AllocateNextNumbersUnderLock()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        Guid id;
        await using (var c = db.Context()) id = (await Service(c).CreateAsync(Create())).Response!.Id;
        async Task<AdminPlanResult> Publish(decimal price)
        {
            await using var c = db.Context();
            return await Service(c).UpdateAsync(id, Update() with { Price = price, FeatureIds = price == 59000 ? [] : [Metro] });
        }
        var results = await Task.WhenAll(Publish(59000), Publish(69000));
        Assert.All(results, r => Assert.Equal(AdminPlanResultStatus.Success, r.Status));
        Assert.Equal(new[] { 2, 3 }, results.Select(r => r.Response!.CurrentVersion!.VersionNumber).Order());
        await using var read = db.Context();
        var history = (await Service(read).GetVersionsAsync(id, new())).Response!;
        Assert.Equal(new[] { 3, 2, 1 }, history.Items.Select(v => v.VersionNumber));
        Assert.Equal(history.Items[0].Id, (await Service(read).GetPlanAsync(id))!.CurrentVersion!.Id);
        Assert.All(history.Items.Take(2), v => Assert.Equal(v.Price == 59000 ? 0 : 1, v.Features.Count));
    }

    [Fact]
    public async Task ConcurrentIdenticalUpdates_SecondIsNoop()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        Guid id;
        await using (var c = db.Context()) id = (await Service(c).CreateAsync(Create())).Response!.Id;
        async Task<AdminPlanResult> Publish()
        {
            await using var c = db.Context();
            return await Service(c).UpdateAsync(id, Update() with { Price = 59000 });
        }
        var results = await Task.WhenAll(Publish(), Publish());
        Assert.All(results, r => Assert.Equal(2, r.Response!.CurrentVersion!.VersionNumber));
        await using var read = db.Context();
        Assert.Equal(2, await read.SubscriptionPlanVersions.CountAsync(v => v.PlanId == id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FeatureInsertFailure_RollsBackCreateOrUpdateAndCurrentPointer(bool create)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        Guid? id = null;
        await using (var c = db.Context())
        {
            if (!create) id = (await Service(c).CreateAsync(Create())).Response!.Id;
            await c.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION s3_fail_feature() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'S3 controlled publication failure' USING ERRCODE='23514'; END; $$;
                CREATE TRIGGER s3_fail_feature BEFORE INSERT ON "SubscriptionPlanVersionFeatures"
                FOR EACH ROW EXECUTE FUNCTION s3_fail_feature();
                """);
            if (create) await Assert.ThrowsAsync<DbUpdateException>(() => Service(c).CreateAsync(Create()));
            else await Assert.ThrowsAsync<DbUpdateException>(() => Service(c).UpdateAsync(id!.Value,
                Update() with { Price = 69000, Name = "Must roll back" }));
        }
        await using var read = db.Context();
        Assert.Equal(create ? 3 : 4, await read.SubscriptionPlans.CountAsync());
        Assert.Equal(create ? 3 : 4, await read.SubscriptionPlanVersions.CountAsync());
        Assert.Equal(create ? 3 : 4, await read.SubscriptionPlanVersionFeatures.CountAsync());
        if (!create)
        {
            var original = (await Service(read).GetPlanAsync(id!.Value))!;
            Assert.Equal("Explorer", original.Name);
            Assert.Equal(1, original.CurrentVersion!.VersionNumber);
            Assert.Equal(49000, original.CurrentVersion.Price);
        }
    }

    [Fact]
    public async Task List_ServerPagingFilteringSortingAndDistinctPeriodCounts()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = Service(c);
        var custom = (await service.CreateAsync(Create())).Response!;
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var v1 = (await new SubscriptionRepository(c).GetVersionAsync(custom.CurrentVersion!.Id))!;
        var order = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, custom.Id, v1);
        var settlement = PlanVersionFoundationPostgresTests.Settlement(c, new PlanVersionFoundationPostgresTests.Clock(Now));
        await settlement.ApplyVerifiedPaymentAsync(new(order.ProviderOrderCode, order.Amount, true));
        // A future same-plan renewal is not a second active subscriber.
        var renewal = await PlanVersionFoundationPostgresTests.BoundOrderAsync(c, user.Id, custom.Id, v1);
        await settlement.ApplyVerifiedPaymentAsync(new(renewal.ProviderOrderCode, renewal.Amount, true));
        var legacyUser = await PlanVersionFoundationPostgresTests.UserAsync(c);
        c.UserSubscriptions.Add(new() { UserId = legacyUser.Id, PlanCode = PlanCode.TripPass, StartsAt = Now, EndsAt = Now.AddDays(7) });
        await c.SaveChangesAsync();
        Assert.Equal(1, (await service.GetPlanAsync(custom.Id))!.ActiveSubscriberCount);
        Assert.Equal(0, (await service.GetPlanAsync(TripPlan))!.ActiveSubscriberCount);
        var repo = new AdminPlanRepository(c);
        Assert.Equal(0, (await repo.GetPlanAsync(custom.Id, Now.AddTicks(-1)))!.ActiveSubscriberCount);
        Assert.Equal(1, (await repo.GetPlanAsync(custom.Id, Now.AddDays(10)))!.ActiveSubscriberCount);
        Assert.Equal(0, (await repo.GetPlanAsync(custom.Id, Now.AddDays(20)))!.ActiveSubscriberCount);
        var filtered = (await service.GetPlansAsync(new() { Search = "explorer", IsActive = false, IsSystem = false })).Response!;
        Assert.Equal(custom.Id, Assert.Single(filtered.Items).Id);
        Assert.Equal(1, filtered.TotalCount);
        var first = (await service.GetPlansAsync(new() { PageSize = 2, SortBy = "name", SortDirection = "desc" })).Response!;
        var second = (await service.GetPlansAsync(new() { Page = 2, PageSize = 2, SortBy = "name", SortDirection = "desc" })).Response!;
        Assert.Equal(4, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        var all = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(4, all.Select(p => p.Id).Distinct().Count());
        Assert.Equal(all.Select(p => p.Name).OrderDescending(), all.Select(p => p.Name));
        Assert.Equal(3, (await service.GetPlansAsync(new() { IsSystem = true, IsActive = true })).Response!.TotalCount);
        Assert.Empty((await service.GetPlansAsync(new() { Page = 10 })).Response!.Items);
    }

    [Fact]
    public async Task Lifecycle_PreservesPendingSnapshotPaidPeriodAndLateIdempotentSettlement()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var admin = Service(c);
        var first = (await admin.CreateAsync(Create())).Response!;
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var pendingUser = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var newUser = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var clock = new PlanVersionFoundationPostgresTests.Clock(Now);
        var gateway = new Gateway();
        var settlement = PlanVersionFoundationPostgresTests.Settlement(c, clock);
        var payment = new PaymentService(new UserRepository(c), new SubscriptionRepository(c), new PaymentOrderRepository(c),
            new PaymentOperationExecutor(c), gateway,
            new PaymentReconciliationService(new PaymentOrderRepository(c), gateway, settlement, clock,
                NullLogger<PaymentReconciliationService>.Instance), clock, NullLogger<PaymentService>.Instance,
            new SubscriptionUpgradeReservationService(new SubscriptionRepository(c), new PaymentOrderRepository(c, clock),
                new PaymentCreditRepository(c, clock), new PaymentOperationExecutor(c), gateway, clock), settlement);
        Assert.Equal(PaymentIntentResultStatus.InvalidPlanCode, (await payment.CheckoutAsync(newUser.Id, first.Code)).Status);
        Assert.Equal(AdminPlanResultStatus.Success, (await admin.SetStatusAsync(first.Id, true)).Status);
        Assert.Equal(PaymentIntentResultStatus.Success, (await payment.CheckoutAsync(user.Id, first.Code)).Status);
        Assert.Equal(PaymentIntentResultStatus.Success, (await payment.CheckoutAsync(pendingUser.Id, first.Code)).Status);
        var orders = await c.PaymentOrders.AsNoTracking().ToListAsync();
        var paid = orders.Single(o => o.UserId == user.Id);
        var pending = orders.Single(o => o.UserId == pendingUser.Id);
        await settlement.ApplyVerifiedPaymentAsync(new(paid.ProviderOrderCode, paid.Amount, true));
        var original = await c.SubscriptionPeriods.AsNoTracking().SingleAsync();
        await admin.UpdateAsync(first.Id, Update() with { Price = 69000, DurationDays = 20, GenerateLimit = 1, SavedTripLimit = 2, FeatureIds = [] });
        await admin.SetStatusAsync(first.Id, false);
        Assert.Equal(PaymentIntentResultStatus.InvalidPlanCode, (await payment.CheckoutAsync(newUser.Id, first.Code)).Status);
        Assert.Equal(PaymentIntentResultStatus.NoActiveSubscription, (await payment.RenewAsync(user.Id)).Status);
        Assert.Equal(2, gateway.Creates);
        var effective = await EffectiveSubscriptionResolver.ResolveAsync(new SubscriptionRepository(c), user.Id, Now);
        Assert.Equal(first.CurrentVersion!.Id, effective.Version.Id);
        Assert.Equal(2, effective.Version.GenerateLimit);
        Assert.Equal(4, effective.Version.SavedTripLimit);
        var stillPending = await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == pending.Id);
        Assert.Equal(PaymentOrderStatus.Pending, stillPending.Status);
        Assert.Equal(pending.PlanVersionId, stillPending.PlanVersionId);
        Assert.Equal(pending.Amount, stillPending.Amount);
        Assert.Equal(PaymentSettlementStatus.Settled, (await settlement.ApplyVerifiedPaymentAsync(new(pending.ProviderOrderCode, pending.Amount, true))).Status);
        Assert.Equal(PaymentSettlementStatus.AlreadyPaid, (await settlement.ApplyVerifiedPaymentAsync(new(pending.ProviderOrderCode, pending.Amount, true))).Status);
        var late = await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.UserId == pendingUser.Id);
        Assert.Equal(first.CurrentVersion.Id, late.PlanVersionId);
        Assert.Equal(Now.AddDays(10), late.EndsAt);
        Assert.Equal(original.EndsAt, (await c.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.Id == original.Id)).EndsAt);
        Assert.Equal(AdminPlanResultStatus.Success, (await admin.SetStatusAsync(first.Id, true)).Status);
        Assert.Equal(PaymentIntentResultStatus.Success, (await payment.RenewAsync(user.Id)).Status);
        var renewal = await c.PaymentOrders.AsNoTracking().SingleAsync(o => o.Type == PaymentOrderType.Renewal);
        Assert.Equal(69000, renewal.Amount);
        Assert.Equal(2, (await new SubscriptionRepository(c).GetVersionAsync(renewal.PlanVersionId!.Value))!.VersionNumber);
        Assert.Equal(PaymentIntentResultStatus.Success, (await payment.CheckoutAsync(newUser.Id, first.Code)).Status);
    }

    [Fact]
    public async Task FreeLifecycleAndDelete_ProtectSystemAndPublishedHistory()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = Service(c);
        var free = SubscriptionBaseline.PlanId(PlanCode.Free);
        Assert.Equal(AdminPlanResultStatus.FreeCannotDeactivate, (await service.SetStatusAsync(free, false)).Status);
        Assert.Equal(AdminPlanResultStatus.SystemPlanLocked, (await service.DeleteAsync(free)).Status);
        Assert.Equal(AdminPlanResultStatus.SystemPlanLocked, (await service.DeleteAsync(TripPlan)).Status);
        var custom = (await service.CreateAsync(Create())).Response!;
        Assert.Equal(AdminPlanResultStatus.PlanInUse, (await service.DeleteAsync(custom.Id)).Status);
        Assert.Equal(4, await c.SubscriptionPlanVersions.CountAsync());
        var bare = new SubscriptionPlan { Code = "BARE", Name = "Unpublished", EntitlementPriority = 400, IsActive = false };
        c.SubscriptionPlans.Add(bare); await c.SaveChangesAsync();
        Assert.Equal(AdminPlanResultStatus.InvalidCurrentVersion, (await service.SetStatusAsync(bare.Id, true)).Status);
        Assert.Equal(AdminPlanResultStatus.Success, (await service.DeleteAsync(bare.Id)).Status);
        Assert.Null(await service.GetPlanAsync(bare.Id));
        var edited = await service.UpdateAsync(free, new() { Name = "Free", Price = 0, DurationDays = null,
            GenerateLimit = 2, SavedTripLimit = 1, AiDailyCallLimit = 3, AiExplainCallsPerTripLimit = 1, FeatureIds = [] });
        Assert.Equal(2, edited.Response!.CurrentVersion!.VersionNumber);
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var resolved = await EffectiveSubscriptionResolver.ResolveAsync(new SubscriptionRepository(c), user.Id, Now);
        Assert.Equal(2, resolved.Version.GenerateLimit);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
    }

    [Fact]
    public async Task HttpWithPostgres_ActualListDetailCreateUpdateLifecycleHistory()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var role = new Role { Name = "Plan Manager", NormalizedName = "PLAN MANAGER" };
        role.Permissions.Add(new() { RoleId = role.Id, Permission = Permissions.ManagePlans });
        var user = new User { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            FullName = "Isolated plan manager", Email = "manager@localmate.test", RoleId = role.Id, Role = role };
        c.Users.Add(user); await c.SaveChangesAsync();
        using var host = new AdminPlansHttpTests.Host(Service(c), new UserAccessService(new UserAccessRepository(c)));
        host.Authenticate();
        var created = await host.Client.PostAsJsonAsync("/api/admin/plans", Create());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<AdminPlanResponse>())!;
        Assert.Equal(Metro, Assert.Single(body.CurrentVersion!.Features).Id);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(created.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/admin/plans?isSystem=false&search=explorer")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync(created.Headers.Location, Update() with { FeatureIds = [] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync($"{created.Headers.Location}/status", new { isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{created.Headers.Location}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.DeleteAsync(created.Headers.Location)).StatusCode);
        user.Status = UserStatus.Locked; await c.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(created.Headers.Location)).StatusCode);
        user.Status = UserStatus.Active;
        c.RolePermissions.RemoveRange(await c.RolePermissions.Where(p => p.RoleId == role.Id).ToListAsync());
        await c.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(created.Headers.Location)).StatusCode);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    [InlineData("entitlementPriority")]
    [InlineData("isActive")]
    [InlineData("isSystem")]
    [InlineData("createdAt")]
    public async Task SortWhitelist_AllFieldsPageStablyOnServer(string field)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = Service(c);
        await service.CreateAsync(Create());
        await service.CreateAsync(Create("ANOTHER", 400));
        foreach (var direction in new[] { "asc", "desc" })
        {
            var all = (await service.GetPlansAsync(new() { SortBy = field, SortDirection = direction })).Response!;
            var paged = new List<Guid>();
            for (var page = 1; page <= 3; page++)
                paged.AddRange((await service.GetPlansAsync(new() { SortBy = field, SortDirection = direction,
                    Page = page, PageSize = 2 })).Response!.Items.Select(p => p.Id));
            Assert.Equal(all.Items.Select(p => p.Id), paged);
            Assert.Equal(5, paged.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentCreates_OnlyOneWinnerForUniqueCodeOrPriority(bool sameCode)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        async Task<AdminPlanResult> CreatePlan(CreateAdminPlanRequest request)
        { await using var c = db.Context(); return await Service(c).CreateAsync(request); }
        var results = await Task.WhenAll(CreatePlan(Create()), CreatePlan(Create(sameCode ? "EXPLORER" : "OTHER", sameCode ? 400 : 300)));
        Assert.Single(results, r => r.Status == AdminPlanResultStatus.Success);
        Assert.Single(results, r => r.Status == (sameCode ? AdminPlanResultStatus.CodeExists : AdminPlanResultStatus.PriorityExists));
        await using var read = db.Context();
        Assert.Equal(4, await read.SubscriptionPlans.CountAsync());
        Assert.Equal(4, await read.SubscriptionPlanVersions.CountAsync());
    }

    [Fact]
    public async Task FeatureValidationAndZeroFeatures_DoNotChangeCatalogOrPublishInvalidSelection()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var service = Service(c);
        foreach (var ids in new[] { new[] { Metro, Metro }, new[] { Guid.NewGuid() } })
            Assert.Equal(AdminPlanResultStatus.ValidationFailed, (await service.CreateAsync(Create() with { FeatureIds = ids })).Status);
        Assert.Equal(3, await c.SubscriptionPlans.CountAsync());
        var zero = (await service.CreateAsync(Create() with { FeatureIds = [], GenerateLimit = 0, SavedTripLimit = 0 })).Response!;
        Assert.Empty(zero.CurrentVersion!.Features);
        Assert.Single(await service.GetFeaturesAsync());
        var consumer = new SubscriptionService(new UserRepository(c), new SubscriptionRepository(c), new UsageEventRepository(c),
            new TripRepository(c), new PlanVersionFoundationPostgresTests.Clock(Now),
            new AiUsageAdmissionRepository(c, new FakeSystemSettingProvider(), TimeProvider.System), new FakeSystemSettingProvider());
        await service.SetStatusAsync(zero.Id, true);
        Assert.Equal(new[] { "Free", "TripPass", "Membership" }, (await consumer.GetPlansAsync()).Select(p => p.Code));
    }

    [Fact]
    public async Task UnpublishedButOrderReferencedPlan_CannotBeDeleted()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var bare = new SubscriptionPlan { Code = "BARE", Name = "Unpublished", EntitlementPriority = 400, IsActive = false };
        c.SubscriptionPlans.Add(bare); await c.SaveChangesAsync();
        var user = await PlanVersionFoundationPostgresTests.UserAsync(c);
        var order = new PaymentOrder { UserId = user.Id, PlanId = bare.Id, PlanVersionBinding = PlanVersionBinding.LegacyUnresolved,
            Amount = 49000, Status = PaymentOrderStatus.Pending, Type = PaymentOrderType.Purchase, ExpiresAt = Now.AddMinutes(15) };
        await new PaymentOrderRepository(c).AddAsync(order);
        Assert.Equal(AdminPlanResultStatus.PlanInUse, (await Service(c).DeleteAsync(bare.Id)).Status);
        Assert.NotNull(await Service(c).GetPlanAsync(bare.Id));
        Assert.Equal(49000, (await c.PaymentOrders.AsNoTracking().SingleAsync()).Amount);
    }

    private sealed class Gateway : IPaymentGateway
    {
        internal int Creates;
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest request, CancellationToken ct = default)
        { Creates++; return Task.FromResult(PaymentLinkResult.Succeeded("https://example.invalid/fixture", "test-only")); }
        public Task<PaymentGatewayOrderResult> GetPaymentAsync(long code, CancellationToken ct = default) => Task.FromResult(PaymentGatewayOrderResult.Unavailable(code));
        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string raw, CancellationToken ct = default) => Task.FromResult(PaymentWebhookVerificationResult.Invalid());
    }
}
