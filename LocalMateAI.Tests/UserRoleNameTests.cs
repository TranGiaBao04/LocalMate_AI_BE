using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class UserRoleNameTests
{
    [Fact]
    public void RoleName_RoleLoaded_ReturnsRoleName()
    {
        var user = new User { Role = new Role { Name = "Kế toán" } };

        Assert.Equal("Kế toán", user.RoleName);
    }

    [Fact]
    public void RoleName_RoleNotLoaded_Throws()
    {
        var user = new User { RoleId = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(() => user.RoleName);
    }
}
