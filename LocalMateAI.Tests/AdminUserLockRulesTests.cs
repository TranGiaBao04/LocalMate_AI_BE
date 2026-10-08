using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminUserLockRulesTests
{
    private static readonly Guid Me = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Other = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    [Theory]
    // người xem có ManageRoles
    [InlineData(true, false, UserStatus.Active, false, true)]
    [InlineData(true, true, UserStatus.Active, false, true)]
    [InlineData(true, false, UserStatus.Locked, false, false)]
    // người xem chỉ có ManageUsers
    [InlineData(false, false, UserStatus.Active, false, true)]
    [InlineData(false, true, UserStatus.Active, false, false)]
    // chính mình
    [InlineData(true, true, UserStatus.Active, true, false)]
    public void CanLock(bool viewerManagesRoles, bool targetManagesRoles, UserStatus status, bool self, bool expected)
    {
        var actor = new AdminActor(Me, viewerManagesRoles);

        Assert.Equal(expected, AdminUserLockRules.CanLock(self ? Me : Other, status, targetManagesRoles, actor));
    }

    [Theory]
    [InlineData(true, true, UserStatus.Locked, true)]
    [InlineData(false, false, UserStatus.Locked, true)]
    [InlineData(false, true, UserStatus.Locked, false)]
    [InlineData(true, false, UserStatus.Active, false)]
    public void CanUnlock(bool viewerManagesRoles, bool targetManagesRoles, UserStatus status, bool expected)
    {
        Assert.Equal(expected,
            AdminUserLockRules.CanUnlock(status, targetManagesRoles, new AdminActor(Me, viewerManagesRoles)));
    }
}
