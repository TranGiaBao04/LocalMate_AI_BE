using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class AdminUserRepositoryPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ResolvesCurrentPlan_LikeEffectiveSubscriptionResolver()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var roleId = await TestRoles.GetUserRoleIdAsync(c);

        var free = await AddUserAsync(c, roleId, "free@test.dev");
        var tripPass = await AddUserAsync(c, roleId, "trippass@test.dev");
        var expired = await AddUserAsync(c, roleId, "expired@test.dev");
        var future = await AddUserAsync(c, roleId, "future@test.dev");
        var overlap = await AddUserAsync(c, roleId, "overlap@test.dev");
        var terminated = await AddUserAsync(c, roleId, "terminated@test.dev");

        await AddPeriodAsync(c, tripPass, PlanCode.TripPass, Now.AddDays(-1), Now.AddDays(6));
        await AddPeriodAsync(c, expired, PlanCode.Membership, Now.AddDays(-30), Now.AddSeconds(-1));
        await AddPeriodAsync(c, future, PlanCode.Membership, Now.AddSeconds(1), Now.AddDays(30));
        await AddPeriodAsync(c, overlap, PlanCode.TripPass, Now.AddDays(-2), Now.AddDays(5));
        await AddPeriodAsync(c, overlap, PlanCode.Membership, Now.AddDays(-1), Now.AddDays(29));
        var terminatedPeriod = await AddPeriodAsync(c, terminated, PlanCode.Membership, Now.AddDays(-5), Now.AddDays(25));
        await TerminateAsync(c, terminatedPeriod, Now.AddMinutes(-1));

        var page = await new AdminUserRepository(c).GetPagedAsync(
            new AdminUserQuery { PageSize = 100, SortBy = "email", SortDirection = "asc" }, Now);

        var byEmail = page.Items.ToDictionary(item => item.Email);
        AssertPlan(byEmail["free@test.dev"], "Free", "Free", null);
        AssertPlan(byEmail["trippass@test.dev"], "TripPass", "Trip Pass", Now.AddDays(6));
        AssertPlan(byEmail["expired@test.dev"], "Free", "Free", null);
        AssertPlan(byEmail["future@test.dev"], "Free", "Free", null);
        AssertPlan(byEmail["overlap@test.dev"], "Membership", "Membership", Now.AddDays(29));
        AssertPlan(byEmail["terminated@test.dev"], "Free", "Free", null);
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(free.Id, byEmail["free@test.dev"].Id);
        Assert.Equal("User", byEmail["free@test.dev"].Role.Name);
        Assert.Equal(roleId, byEmail["free@test.dev"].Role.Id);
    }

    [Fact]
    public async Task FiltersByPlanStatusRoleAndSearch_BeforePaging()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var userRoleId = await TestRoles.GetUserRoleIdAsync(c);
        var otherRole = new Role { Name = "CSKH", NormalizedName = "CSKH" };
        c.Roles.Add(otherRole);
        await c.SaveChangesAsync();

        await AddUserAsync(c, userRoleId, "an@test.dev", "Nguyễn Văn An");
        var member = await AddUserAsync(c, userRoleId, "binh@test.dev", "Trần Bình");
        var locked = await AddUserAsync(c, userRoleId, "chi@test.dev", "Lê Chi", UserStatus.Locked);
        await AddUserAsync(c, otherRole.Id, "dung_100%@test.dev", "Phạm Dũng");
        await AddUserAsync(c, otherRole.Id, "dungx100@test.dev", "Phạm Dũng X");
        await AddPeriodAsync(c, member, PlanCode.Membership, Now.AddDays(-1), Now.AddDays(29));
        await AddPeriodAsync(c, locked, PlanCode.Membership, Now.AddDays(-1), Now.AddDays(29));
        var repository = new AdminUserRepository(c);

        var members = await repository.GetPagedAsync(new AdminUserQuery { Plan = "membership", PageSize = 1 }, Now);
        Assert.Equal(2, members.TotalCount);
        Assert.Equal(2, members.TotalPages);
        Assert.Single(members.Items);

        var free = await repository.GetPagedAsync(new AdminUserQuery { Plan = " FREE " }, Now);
        Assert.Equal(["an@test.dev", "dung_100%@test.dev", "dungx100@test.dev"], free.Items.Select(i => i.Email).Order());

        var lockedMembers = await repository.GetPagedAsync(
            new AdminUserQuery { Plan = "MEMBERSHIP", Status = UserStatus.Locked }, Now);
        Assert.Equal(locked.Id, Assert.Single(lockedMembers.Items).Id);
        Assert.Equal("Locked", lockedMembers.Items[0].Status);

        var otherRoleUsers = await repository.GetPagedAsync(new AdminUserQuery { RoleId = otherRole.Id }, Now);
        Assert.Equal(2, otherRoleUsers.TotalCount);
        Assert.All(otherRoleUsers.Items, item => Assert.Equal("CSKH", item.Role.Name));

        // "%" và "_" là ký tự thường, không phải ký tự đại diện của LIKE.
        var escaped = await repository.GetPagedAsync(new AdminUserQuery { Search = "_100%" }, Now);
        Assert.Equal("dung_100%@test.dev", Assert.Single(escaped.Items).Email);

        var byName = await repository.GetPagedAsync(new AdminUserQuery { Search = "  trần " }, Now);
        Assert.Equal(member.Id, Assert.Single(byName.Items).Id);

        Assert.Empty((await repository.GetPagedAsync(new AdminUserQuery { Plan = "GOLD" }, Now)).Items);
        Assert.Empty((await repository.GetPagedAsync(new AdminUserQuery { RoleId = Guid.NewGuid() }, Now)).Items);
    }

    [Fact]
    public async Task Search_IgnoresVietnameseDiacritics_AndManagesRolesFollowsPermissions()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var userRoleId = await TestRoles.GetUserRoleIdAsync(c);
        var adminRoleId = await c.Roles.Where(role => role.IsSystem && role.NormalizedName == "ADMIN")
            .Select(role => role.Id).SingleAsync();
        var lead = new Role { Name = "Trưởng nhóm", NormalizedName = "TRƯỞNG NHÓM" };
        lead.Permissions.Add(new RolePermission { RoleId = lead.Id, Permission = Permissions.ManageRoles });
        var support = new Role { Name = "CSKH", NormalizedName = "CSKH" };
        support.Permissions.Add(new RolePermission { RoleId = support.Id, Permission = Permissions.ManageUsers });
        c.Roles.AddRange(lead, support);
        await c.SaveChangesAsync();

        await AddUserAsync(c, userRoleId, "an@test.dev", "Nguyễn Đức An");
        await AddUserAsync(c, adminRoleId, "boss@test.dev", "Duc Admin");
        await AddUserAsync(c, lead.Id, "lead@test.dev", "Trần Thị Hằng");
        await AddUserAsync(c, support.Id, "cskh@test.dev", "Lê Văn Bình");
        var repository = new AdminUserRepository(c);

        var unaccented = await repository.GetPagedAsync(new AdminUserQuery { Search = "nguyen duc" }, Now);
        Assert.Equal("an@test.dev", Assert.Single(unaccented.Items).Email);

        var accented = await repository.GetPagedAsync(
            new AdminUserQuery { Search = "Đức", SortBy = "email", SortDirection = "asc" }, Now);
        Assert.Equal(["an@test.dev", "boss@test.dev"], accented.Items.Select(item => item.Email));

        Assert.Equal("lead@test.dev", Assert.Single(
            (await repository.GetPagedAsync(new AdminUserQuery { Search = "HANG" }, Now)).Items).Email);

        var all = (await repository.GetPagedAsync(new AdminUserQuery(), Now)).Items.ToDictionary(item => item.Email);
        Assert.True(all["boss@test.dev"].ManagesRoles);
        Assert.True(all["lead@test.dev"].ManagesRoles);
        Assert.False(all["cskh@test.dev"].ManagesRoles);
        Assert.False(all["an@test.dev"].ManagesRoles);

        Assert.True((await repository.GetDetailAsync(all["lead@test.dev"].Id))!.ManagesRoles);
        Assert.False((await repository.GetDetailAsync(all["cskh@test.dev"].Id))!.ManagesRoles);
    }

    [Fact]
    public async Task FilterOptions_ListAllRolesAndPlans_IncludingInactivePlans()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        c.Roles.Add(new Role { Name = "CSKH", NormalizedName = "CSKH" });
        c.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Code = "EXPLORER", Name = "Explorer", EntitlementPriority = 7000, IsActive = false, IsSystem = false
        });
        await c.SaveChangesAsync();

        var options = await new AdminUserRepository(c).GetFilterOptionsAsync();

        Assert.Equal(["Admin", "User", "CSKH"], options.Roles.Select(role => role.Name));
        Assert.Equal(
            [("Free", "Free", true), ("TripPass", "Trip Pass", true), ("Membership", "Membership", true),
                ("EXPLORER", "Explorer", false)],
            options.Plans.Select(plan => (plan.Code, plan.Name, plan.IsActive)));
    }

    [Fact]
    public async Task DefaultSort_IsNewestFirst()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var roleId = await TestRoles.GetUserRoleIdAsync(c);
        await AddUserAsync(c, roleId, "old@test.dev", createdAt: Now.AddDays(-2));
        await AddUserAsync(c, roleId, "new@test.dev", createdAt: Now.AddDays(-1));

        var page = await new AdminUserRepository(c).GetPagedAsync(new AdminUserQuery(), Now);

        Assert.Equal(["new@test.dev", "old@test.dev"], page.Items.Select(item => item.Email));
    }

    [Fact]
    public async Task Detail_ReportsLockerProvidersAndStats()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var roleId = await TestRoles.GetUserRoleIdAsync(c);
        var admin = await AddUserAsync(c, roleId, "admin@test.dev", "Quản trị");
        var user = await AddUserAsync(c, roleId, "an@test.dev", "An", UserStatus.Locked);
        await c.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(u => u.LockedByUserId, admin.Id)
            .SetProperty(u => u.LockReason, "spam")
            .SetProperty(u => u.PasswordHash, (string?)null));
        c.UserExternalLogins.Add(new UserExternalLogin { UserId = user.Id, Provider = "Google", ProviderSubject = "g-1" });
        c.Trips.AddRange(
            Trip(user.Id, TripStatus.Draft),
            Trip(user.Id, TripStatus.Finalized),
            Trip(user.Id, TripStatus.Finalized, deletedAt: Now),
            Trip(admin.Id, TripStatus.Finalized));
        c.PaymentOrders.AddRange(
            Order(user.Id, 9001, PaymentOrderStatus.Paid, 19000),
            Order(user.Id, 9002, PaymentOrderStatus.Paid, 59000),
            Order(user.Id, 9003, PaymentOrderStatus.Pending, 59000),
            Order(admin.Id, 9004, PaymentOrderStatus.Paid, 99000));
        await c.SaveChangesAsync();

        var detail = await new AdminUserRepository(c).GetDetailAsync(user.Id);

        Assert.NotNull(detail);
        Assert.Equal(UserStatus.Locked, detail.Status);
        Assert.Equal("spam", detail.LockReason);
        Assert.Equal(new AdminUserLockedByResponse(admin.Id, "Quản trị", "admin@test.dev"), detail.LockedBy);
        Assert.False(detail.HasPassword);
        Assert.Equal(["Google"], detail.LoginProviders);
        Assert.Equal(new AdminUserStatsResponse(2, 1, 1, 2, 78000, 0), detail.Stats);
        Assert.Null(await new AdminUserRepository(c).GetDetailAsync(Guid.NewGuid()));
        Assert.False(await new AdminUserRepository(c).ExistsAsync(Guid.NewGuid()));
        Assert.True(await new AdminUserRepository(c).ExistsAsync(user.Id));
    }

    [Fact]
    public async Task Trips_FilterDeletedAndStatus_AndSplitPlannedStart()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var roleId = await TestRoles.GetUserRoleIdAsync(c);
        var user = await AddUserAsync(c, roleId, "an@test.dev");
        var other = await AddUserAsync(c, roleId, "binh@test.dev");
        var place = AddPlace(c);
        var planned = Trip(user.Id, TripStatus.Finalized, plannedStartAt: new DateTime(2026, 10, 20, 8, 30, 0));
        planned.Items.Add(Item(place, 0, 30000));
        planned.Items.Add(Item(place, 1, 45000));
        var draft = Trip(user.Id, TripStatus.Draft);
        var deleted = Trip(user.Id, TripStatus.Draft, deletedAt: Now);
        c.Trips.AddRange(planned, draft, deleted, Trip(other.Id, TripStatus.Draft));
        await c.SaveChangesAsync();
        var repository = new AdminUserRepository(c);

        var visible = await repository.GetTripsAsync(user.Id, new AdminUserTripQuery());
        Assert.Equal(2, visible.TotalCount);
        Assert.DoesNotContain(visible.Items, trip => trip.Id == deleted.Id);

        var all = await repository.GetTripsAsync(user.Id, new AdminUserTripQuery { IncludeDeleted = true });
        Assert.Equal(3, all.TotalCount);
        Assert.NotNull(all.Items.Single(trip => trip.Id == deleted.Id).DeletedAt);

        var finalized = Assert.Single((await repository.GetTripsAsync(
            user.Id, new AdminUserTripQuery { Status = TripStatus.Finalized })).Items);
        Assert.Equal(planned.Id, finalized.Id);
        Assert.Equal(new DateOnly(2026, 10, 20), finalized.PlannedDate);
        Assert.Equal(new TimeOnly(8, 30), finalized.StartTime);
        Assert.Equal(2, finalized.ItemCount);
        Assert.Equal(75000, finalized.EstimatedBudget);
        Assert.Equal("Finalized", finalized.Status);

        var draftItem = (await repository.GetTripsAsync(user.Id, new AdminUserTripQuery { Status = TripStatus.Draft }))
            .Items.Single();
        Assert.Null(draftItem.PlannedDate);
        Assert.Null(draftItem.StartTime);
        Assert.Equal(0, draftItem.ItemCount);
    }

    [Fact]
    public async Task TransactionRepository_UserFilter_ReturnsOnlyThatUsersOrders()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var roleId = await TestRoles.GetUserRoleIdAsync(c);
        // Email user khác chứa email user cần xem ⇒ tìm theo chuỗi sẽ lẫn, lọc theo UserId thì không.
        var user = await AddUserAsync(c, roleId, "an@test.dev");
        var lookalike = await AddUserAsync(c, roleId, "tran@test.dev");
        c.PaymentOrders.AddRange(
            Order(user.Id, 9101, PaymentOrderStatus.Paid, 19000),
            Order(user.Id, 9102, PaymentOrderStatus.Pending, 59000),
            Order(lookalike.Id, 9103, PaymentOrderStatus.Paid, 59000));
        await c.SaveChangesAsync();
        var repository = new AdminTransactionRepository(c);
        var paging = new PagedQuery { PageSize = 100 };

        var byUser = await repository.GetTransactionsAsync(
            new AdminTransactionFilter(null, null, null, null, null, user.Id), paging);
        Assert.Equal([9101L, 9102L], byUser.Items.Select(o => o.ProviderOrderCode).Order());

        var paidByUser = await repository.GetTransactionsAsync(
            new AdminTransactionFilter(null, PaymentOrderStatus.Paid, null, null, null, user.Id), paging);
        Assert.Equal(9101L, Assert.Single(paidByUser.Items).ProviderOrderCode);

        var everyone = await repository.GetTransactionsAsync(new AdminTransactionFilter(null, null, null, null, null), paging);
        Assert.Equal(3, everyone.TotalCount);
    }

    private static Place AddPlace(AppDbContext c)
    {
        var place = new Place
        {
            Name = "Chợ Bến Thành",
            Address = "Quận 1",
            Location = new Point(106.698, 10.772) { SRID = 4326 },
            Category = PlaceCategory.CheckIn,
            Status = PlaceStatus.Active
        };
        c.Places.Add(place);
        return place;
    }

    private static Trip Trip(Guid userId, TripStatus status, DateTime? deletedAt = null, DateTime? plannedStartAt = null) => new()
    {
        UserId = userId,
        StartLatitude = 10.772,
        StartLongitude = 106.698,
        DurationHours = 4,
        BudgetMax = 300000,
        Status = status,
        FinalizedAt = status == TripStatus.Finalized ? Now : null,
        DeletedAt = deletedAt,
        PlannedStartAt = plannedStartAt
    };

    private static ItineraryItem Item(Place place, int orderIndex, decimal budget) => new()
    {
        Place = place,
        OrderIndex = orderIndex,
        ScheduledTime = new TimeOnly(9, 0).AddHours(orderIndex),
        EstimatedDurationMinutes = 45,
        EstimatedBudget = budget
    };

    // LegacyUnresolved: trigger của bảng PaymentOrders chỉ bắt đơn Native Paid phải có kỳ gói.
    private static PaymentOrder Order(Guid userId, long code, PaymentOrderStatus status, decimal amount) => new()
    {
        UserId = userId,
        ProviderOrderCode = code,
        PlanCode = PlanCode.TripPass,
        PlanVersionBinding = PlanVersionBinding.LegacyUnresolved,
        Type = PaymentOrderType.Purchase,
        Status = status,
        Amount = amount,
        ExpiresAt = Now.AddMinutes(15),
        PaidAt = status == PaymentOrderStatus.Paid ? Now : null
    };

    private static void AssertPlan(AdminUserListItemResponse item, string code, string name, DateTime? until)
    {
        Assert.Equal(code, item.Plan.Code);
        Assert.Equal(name, item.Plan.Name);
        Assert.Equal(until, item.PlanEffectiveUntil);
    }

    private static async Task<User> AddUserAsync(AppDbContext c, Guid roleId, string email, string? fullName = null,
        UserStatus status = UserStatus.Active, DateTime? createdAt = null)
    {
        var user = new User
        {
            Email = email,
            FullName = fullName ?? email,
            PasswordHash = "hash",
            RoleId = roleId,
            Status = status,
            LockedAt = status == UserStatus.Locked ? Now : null
        };
        c.Users.Add(user);
        await c.SaveChangesAsync();
        if (createdAt is { } at)
        {
            await c.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.CreatedAt, at));
        }

        return user;
    }

    // Kỳ gói dựng qua nguồn legacy: trigger của bảng kỳ gói chỉ cho nguồn là đơn đã trả hoặc UserSubscription khớp.
    private static async Task<SubscriptionPeriod> AddPeriodAsync(AppDbContext c, User user, PlanCode plan,
        DateTime startsAt, DateTime endsAt)
    {
        var legacy = new UserSubscription { UserId = user.Id, PlanCode = plan, StartsAt = startsAt, EndsAt = endsAt };
        c.UserSubscriptions.Add(legacy);
        await c.SaveChangesAsync();
        var period = new SubscriptionPeriod
        {
            UserId = user.Id,
            PlanId = SubscriptionBaseline.PlanId(plan),
            PlanVersionId = SubscriptionBaseline.VersionId(plan),
            StartsAt = startsAt,
            EndsAt = endsAt,
            LegacyUserSubscriptionId = legacy.Id
        };
        c.SubscriptionPeriods.Add(period);
        await c.SaveChangesAsync();
        return period;
    }

    // Chấm dứt thật cần cả chuỗi đơn nâng cấp + credit; DB test dùng một lần nên tắt trigger để dựng nhanh trạng thái.
    private static async Task TerminateAsync(AppDbContext c, SubscriptionPeriod period, DateTime terminatedAt)
    {
        await c.Database.ExecuteSqlRawAsync("ALTER TABLE \"SubscriptionPeriods\" DISABLE TRIGGER USER");
        await c.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"SubscriptionPeriods\" DROP CONSTRAINT IF EXISTS \"CK_Periods_Termination\"");
        await c.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SubscriptionPeriods\" SET \"TerminatedAt\" = {terminatedAt} WHERE \"Id\" = {period.Id}");
        await c.Database.ExecuteSqlRawAsync("ALTER TABLE \"SubscriptionPeriods\" ENABLE TRIGGER USER");
    }
}
