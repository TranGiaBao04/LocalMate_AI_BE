using LocalMateAI.Application.DTOs.Roles;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminRoleServiceTests
{
    private static readonly Guid ActorId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // ---------- Tạo role ----------

    [Fact]
    public async Task Create_ValidRequest_TrimsAndDeduplicatesPermissions()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateRoleAsync(new SaveRoleRequest(
            "  Kế toán  ",
            "  Chỉ xem doanh thu  ",
            [Permissions.ViewRevenue, " ViewRevenue ", Permissions.ManageUsers]));

        Assert.Equal(AdminRoleResultStatus.Success, result.Status);
        Assert.Equal("Kế toán", result.Response!.Name);
        Assert.Equal("Chỉ xem doanh thu", result.Response.Description);
        Assert.False(result.Response.IsSystem);
        Assert.Equal([Permissions.ViewRevenue, Permissions.ManageUsers], result.Response.Permissions);
        Assert.Equal(SystemRoles.Normalize("Kế toán"), fixture.Repository.Roles[result.Response.Id].NormalizedName);
        Assert.Equal([AdminLocks.RoleManagement], fixture.Executor.LockNames);
    }

    [Theory]
    [InlineData("", null, "name")]
    [InlineData("   ", null, "name")]
    [InlineData("123456789012345678901234567890123456789012345678901", null, "name")]
    [InlineData("Kế toán", "DESCRIPTION_TOO_LONG", "description")]
    public async Task Create_InvalidNameOrDescription_ReturnsFieldError(string name, string? description, string field)
    {
        var fixture = new Fixture();
        if (description == "DESCRIPTION_TOO_LONG")
        {
            description = new string('a', AdminRoleService.MaxDescriptionLength + 1);
        }

        var result = await fixture.Service.CreateRoleAsync(new SaveRoleRequest(name, description, []));

        Assert.Equal(AdminRoleResultStatus.ValidationFailed, result.Status);
        Assert.Contains(field, result.ValidationErrors!.Keys);
        Assert.Empty(fixture.Executor.LockNames);
    }

    [Fact]
    public async Task Create_UnknownPermission_ReturnsPermissionsError()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateRoleAsync(new SaveRoleRequest("Kế toán", null, ["DeleteEverything"]));

        Assert.Equal(AdminRoleResultStatus.ValidationFailed, result.Status);
        Assert.Contains("DeleteEverything", Assert.Single(result.ValidationErrors!["permissions"]));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData(" User ")]
    public async Task Create_NameOfSystemRole_ReturnsNameExists(string name)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateRoleAsync(new SaveRoleRequest(name, null, []));

        Assert.Equal(AdminRoleResultStatus.NameExists, result.Status);
    }

    // ---------- Sửa role ----------

    [Fact]
    public async Task Update_MissingRole_ReturnsNotFound()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UpdateRoleAsync(Guid.NewGuid(), new SaveRoleRequest("X", null, []), ActorId);

        Assert.Equal(AdminRoleResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Update_SystemRole_ReturnsSystemRoleLocked()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UpdateRoleAsync(
            fixture.UserRole.Id,
            new SaveRoleRequest("Người dùng", null, []),
            ActorId);

        Assert.Equal(AdminRoleResultStatus.SystemRoleLocked, result.Status);
    }

    [Fact]
    public async Task Update_NameUsedByAnotherRole_ReturnsNameExists()
    {
        var fixture = new Fixture();
        var first = fixture.AddCustomRole("Kế toán");
        fixture.AddCustomRole("Biên tập");

        var result = await fixture.Service.UpdateRoleAsync(first.Id, new SaveRoleRequest("biên tập", null, []), ActorId);

        Assert.Equal(AdminRoleResultStatus.NameExists, result.Status);
    }

    [Fact]
    public async Task Update_SameNameDifferentCase_Succeeds()
    {
        var fixture = new Fixture();
        var role = fixture.AddCustomRole("Kế toán");

        var result = await fixture.Service.UpdateRoleAsync(role.Id, new SaveRoleRequest("KẾ TOÁN", null, []), ActorId);

        Assert.Equal(AdminRoleResultStatus.Success, result.Status);
        Assert.Equal("KẾ TOÁN", result.Response!.Name);
    }

    [Fact]
    public async Task Update_RemoveManageRolesFromActorsOwnRole_ReturnsCannotRemoveOwnRoleManager()
    {
        var fixture = new Fixture();
        var managerRole = fixture.AddCustomRole("Quản trị phụ", Permissions.ManageRoles);
        fixture.AddUser(ActorId, managerRole.Id);
        fixture.AddUser(OtherUserId, fixture.AdminRole.Id);

        var result = await fixture.Service.UpdateRoleAsync(
            managerRole.Id,
            new SaveRoleRequest("Quản trị phụ", null, [Permissions.ViewRevenue]),
            ActorId);

        Assert.Equal(AdminRoleResultStatus.CannotRemoveOwnRoleManager, result.Status);
        Assert.Contains(managerRole.Permissions, permission => permission.Permission == Permissions.ManageRoles);
    }

    [Fact]
    public async Task Update_RemoveManageRolesFromLastManagerRole_ReturnsLastRoleManager()
    {
        var fixture = new Fixture();
        var managerRole = fixture.AddCustomRole("Quản trị phụ", Permissions.ManageRoles);
        fixture.AddUser(OtherUserId, managerRole.Id);

        var result = await fixture.Service.UpdateRoleAsync(
            managerRole.Id,
            new SaveRoleRequest("Quản trị phụ", null, []),
            ActorId);

        Assert.Equal(AdminRoleResultStatus.LastRoleManager, result.Status);
    }

    [Fact]
    public async Task Update_Success_SyncsPermissionsAndKeepsUnchangedOnes()
    {
        var fixture = new Fixture();
        var role = fixture.AddCustomRole("Kế toán", Permissions.ViewRevenue, Permissions.ManageUsers);
        var keptPermission = role.Permissions.Single(permission => permission.Permission == Permissions.ViewRevenue);

        var result = await fixture.Service.UpdateRoleAsync(
            role.Id,
            new SaveRoleRequest("Kế toán", "Mới", [Permissions.ViewRevenue, Permissions.ManagePlans]),
            ActorId);

        Assert.Equal(AdminRoleResultStatus.Success, result.Status);
        Assert.Equal([Permissions.ManagePlans, Permissions.ViewRevenue], result.Response!.Permissions);
        Assert.Contains(keptPermission, role.Permissions);
        Assert.Equal("Mới", role.Description);
        Assert.Equal(1, fixture.Repository.SaveCalls);
    }

    // ---------- Xoá role ----------

    [Fact]
    public async Task Delete_MissingRole_ReturnsNotFound()
    {
        var fixture = new Fixture();

        Assert.Equal(AdminRoleResultStatus.NotFound, (await fixture.Service.DeleteRoleAsync(Guid.NewGuid())).Status);
    }

    [Fact]
    public async Task Delete_SystemRole_ReturnsSystemRoleLocked()
    {
        var fixture = new Fixture();

        Assert.Equal(
            AdminRoleResultStatus.SystemRoleLocked,
            (await fixture.Service.DeleteRoleAsync(fixture.AdminRole.Id)).Status);
    }

    [Fact]
    public async Task Delete_RoleWithUsers_ReturnsRoleInUse()
    {
        var fixture = new Fixture();
        var role = fixture.AddCustomRole("Kế toán");
        fixture.AddUser(OtherUserId, role.Id);

        Assert.Equal(AdminRoleResultStatus.RoleInUse, (await fixture.Service.DeleteRoleAsync(role.Id)).Status);
        Assert.True(fixture.Repository.Roles.ContainsKey(role.Id));
    }

    [Fact]
    public async Task Delete_UnusedCustomRole_Succeeds()
    {
        var fixture = new Fixture();
        var role = fixture.AddCustomRole("Kế toán");

        Assert.Equal(AdminRoleResultStatus.Success, (await fixture.Service.DeleteRoleAsync(role.Id)).Status);
        Assert.False(fixture.Repository.Roles.ContainsKey(role.Id));
    }

    // ---------- Gán role ----------

    [Fact]
    public async Task Assign_EmptyRoleId_ReturnsInvalidRoleWithoutLock()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AssignUserRoleAsync(OtherUserId, new AssignUserRoleRequest(Guid.Empty), ActorId);

        Assert.Equal(AssignUserRoleResultStatus.InvalidRole, result.Status);
        Assert.Empty(fixture.Executor.LockNames);
    }

    [Fact]
    public async Task Assign_OwnAccount_ReturnsCannotChangeOwnRole()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AssignUserRoleAsync(
            ActorId,
            new AssignUserRoleRequest(fixture.UserRole.Id),
            ActorId);

        Assert.Equal(AssignUserRoleResultStatus.CannotChangeOwnRole, result.Status);
    }

    [Fact]
    public async Task Assign_MissingUser_ReturnsUserNotFound()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AssignUserRoleAsync(
            OtherUserId,
            new AssignUserRoleRequest(fixture.UserRole.Id),
            ActorId);

        Assert.Equal(AssignUserRoleResultStatus.UserNotFound, result.Status);
    }

    [Fact]
    public async Task Assign_MissingRole_ReturnsInvalidRole()
    {
        var fixture = new Fixture();
        fixture.AddUser(OtherUserId, fixture.UserRole.Id);

        var result = await fixture.Service.AssignUserRoleAsync(
            OtherUserId,
            new AssignUserRoleRequest(Guid.NewGuid()),
            ActorId);

        Assert.Equal(AssignUserRoleResultStatus.InvalidRole, result.Status);
    }

    [Fact]
    public async Task Assign_DemoteLastActiveManager_ReturnsLastRoleManager()
    {
        var fixture = new Fixture();
        fixture.AddUser(OtherUserId, fixture.AdminRole.Id);

        var result = await fixture.Service.AssignUserRoleAsync(
            OtherUserId,
            new AssignUserRoleRequest(fixture.UserRole.Id),
            ActorId);

        Assert.Equal(AssignUserRoleResultStatus.LastRoleManager, result.Status);
        Assert.Empty(fixture.Repository.Assignments);
    }

    [Fact]
    public async Task Assign_DemoteManagerWhenAnotherActiveManagerExists_Succeeds()
    {
        var fixture = new Fixture();
        fixture.AddUser(ActorId, fixture.AdminRole.Id);
        fixture.AddUser(OtherUserId, fixture.AdminRole.Id);
        var accountant = fixture.AddCustomRole("Kế toán", Permissions.ViewRevenue);

        var result = await fixture.Service.AssignUserRoleAsync(
            OtherUserId,
            new AssignUserRoleRequest(accountant.Id),
            ActorId);

        Assert.Equal(AssignUserRoleResultStatus.Success, result.Status);
        Assert.Equal(new UserRoleAssignmentResponse(OtherUserId, accountant.Id, "Kế toán"), result.Response);
        Assert.Equal([(OtherUserId, accountant.Id)], fixture.Repository.Assignments);
    }

    [Fact]
    public async Task Assign_LockedManager_IsNotCountedAsManager()
    {
        var fixture = new Fixture();
        fixture.AddUser(OtherUserId, fixture.AdminRole.Id, UserStatus.Locked);

        var result = await fixture.Service.AssignUserRoleAsync(
            OtherUserId,
            new AssignUserRoleRequest(fixture.UserRole.Id),
            ActorId);

        Assert.Equal(AssignUserRoleResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task Assign_SameRole_SucceedsWithoutWriting()
    {
        var fixture = new Fixture();
        fixture.AddUser(OtherUserId, fixture.UserRole.Id);

        var result = await fixture.Service.AssignUserRoleAsync(
            OtherUserId,
            new AssignUserRoleRequest(fixture.UserRole.Id),
            ActorId);

        Assert.Equal(AssignUserRoleResultStatus.Success, result.Status);
        Assert.Empty(fixture.Repository.Assignments);
    }

    // ---------- Đọc ----------

    [Fact]
    public async Task GetRoles_SystemAdminShowsEveryPermission()
    {
        var fixture = new Fixture();

        var roles = await fixture.Service.GetRolesAsync();

        var admin = Assert.Single(roles, role => role.Id == fixture.AdminRole.Id);
        Assert.Equal(Permissions.All.Select(permission => permission.Code), admin.Permissions);
    }

    [Fact]
    public void GetPermissions_ReturnsCatalog()
    {
        var permissions = new Fixture().Service.GetPermissions();

        Assert.Equal(Permissions.All.Select(permission => permission.Code), permissions.Select(permission => permission.Code));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            AdminRole = AddRole(SystemRoles.AdminName, isSystem: true);
            UserRole = AddRole(SystemRoles.UserName, isSystem: true);
            Service = new AdminRoleService(Repository, Executor);
        }

        public FakeAdminRoleRepository Repository { get; } = new();
        public FakeOperationExecutor Executor { get; } = new();
        public Role AdminRole { get; }
        public Role UserRole { get; }
        public AdminRoleService Service { get; }

        public Role AddCustomRole(string name, params string[] permissions) =>
            AddRole(name, isSystem: false, permissions);

        public void AddUser(Guid userId, Guid roleId, UserStatus status = UserStatus.Active) =>
            Repository.Users[userId] = (roleId, status);

        private Role AddRole(string name, bool isSystem, params string[] permissions)
        {
            var role = new Role { Name = name, NormalizedName = SystemRoles.Normalize(name), IsSystem = isSystem };
            foreach (var permission in permissions)
            {
                role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
            }

            Repository.Roles[role.Id] = role;
            return role;
        }
    }

    private sealed class FakeOperationExecutor : IAdminOperationExecutor
    {
        public List<string> LockNames { get; } = [];

        public Task<T> ExecuteExclusiveAsync<T>(
            string lockName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            LockNames.Add(lockName);
            return operation(cancellationToken);
        }
    }

    private sealed class FakeAdminRoleRepository : IAdminRoleRepository
    {
        public Dictionary<Guid, Role> Roles { get; } = [];
        public Dictionary<Guid, (Guid RoleId, UserStatus Status)> Users { get; } = [];
        public List<(Guid UserId, Guid RoleId)> Assignments { get; } = [];
        public int SaveCalls { get; private set; }

        public Task<IReadOnlyList<RoleReadModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleReadModel>>(Roles.Values.Select(ToReadModel).ToList());

        public Task<RoleReadModel?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Roles.TryGetValue(roleId, out var role) ? ToReadModel(role) : null);

        public Task<Role?> GetForUpdateAsync(Guid roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Roles.GetValueOrDefault(roleId));

        public Task<bool> NormalizedNameExistsAsync(
            string normalizedName,
            Guid? excludeRoleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Roles.Values.Any(role => role.NormalizedName == normalizedName && role.Id != excludeRoleId));

        public Task<bool> TryAddAsync(Role role, CancellationToken cancellationToken = default)
        {
            Roles[role.Id] = role;
            return Task.FromResult(true);
        }

        public Task<bool> TrySaveAsync(Role role, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(true);
        }

        public Task DeleteAsync(Role role, CancellationToken cancellationToken = default)
        {
            Roles.Remove(role.Id);
            return Task.CompletedTask;
        }

        public Task<int> CountUsersAsync(Guid roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.Values.Count(user => user.RoleId == roleId));

        public Task<int> CountActiveRoleManagersAsync(
            Guid? excludeUserId,
            Guid? excludeRoleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.Count(user =>
                user.Value.Status == UserStatus.Active
                && user.Key != excludeUserId
                && user.Value.RoleId != excludeRoleId
                && RolePermissionRules.Resolve(Roles[user.Value.RoleId]).Contains(Permissions.ManageRoles)));

        public Task<UserRoleReadModel?> GetUserRoleAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (!Users.TryGetValue(userId, out var user))
            {
                return Task.FromResult<UserRoleReadModel?>(null);
            }

            var role = Roles[user.RoleId];
            return Task.FromResult<UserRoleReadModel?>(new UserRoleReadModel(
                userId,
                role.Id,
                user.Status,
                role.NormalizedName,
                role.IsSystem,
                role.Permissions.Select(permission => permission.Permission).ToList()));
        }

        public Task SetUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
        {
            Assignments.Add((userId, roleId));
            Users[userId] = (roleId, Users[userId].Status);
            return Task.CompletedTask;
        }

        private RoleReadModel ToReadModel(Role role) =>
            new(
                role.Id,
                role.Name,
                role.NormalizedName,
                role.Description,
                role.IsSystem,
                role.Permissions.Select(permission => permission.Permission).ToList(),
                Users.Values.Count(user => user.RoleId == role.Id));
    }
}
